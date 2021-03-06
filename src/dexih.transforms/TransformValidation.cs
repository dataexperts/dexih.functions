﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dexih.functions;
using System.Text;
using System.Threading;
using dexih.functions.Exceptions;
using dexih.functions.Query;
using dexih.transforms.Exceptions;
using dexih.transforms.Mapping;
using dexih.transforms.Transforms;
using Dexih.Utils.DataType;

namespace dexih.transforms
{
    [Transform(
        Name = "Validation",
        Description = "Validates and cleans/rejects data.",
        TransformType = ETransformType.Validation
    )]
    public class TransformValidation : Transform
    {
        public TransformValidation() { }

        public TransformValidation(Transform inReader, Mappings mappings, bool validateDataTypes)
        {
            Mappings = mappings ?? new Mappings();
            SetInTransform(inReader);
            ValidateDataTypes = validateDataTypes;
        }

        public bool ValidateDataTypes { get; set; } = true;

        private object[] _savedRejectRow; //used as a temporary store for the pass row when a pass and reject occur.

        private bool _lastRecord = false;

        private int _rejectReasonOrdinal;
        private int _operationOrdinal;
        private int _validationStatusOrdinal;

        private List<(int, int)> _mapFieldOrdinals;
        
        private int _primaryFieldCount;
        private int _columnCount;

        private Table _mappingTable;

        public override string TransformName { get; } = "Validation";

        public override Dictionary<string, object> TransformProperties()
        {
            return null;
        }
        
        protected override Table InitializeCacheTable(bool mapAllReferenceColumns)
        {
            var table = new Table("Validate");
            
            _mappingTable = Mappings.Initialize(PrimaryTransform?.CacheTable, ReferenceTransform?.CacheTable, ReferenceTransform?.TableAlias, mapAllReferenceColumns);
            var sourceTable = PrimaryTransform?.CacheTable;
            
            //add the operation type, which indicates whether record is rejected 'R' or 'C/U/D' create/update/delete
            if (sourceTable.Columns.All(c =>  c.DeltaType != EDeltaType.DatabaseOperation))
            {
                table.Columns.Add(new TableColumn("Operation")
                {
                    DeltaType = EDeltaType.DatabaseOperation,
                    AllowDbNull = true
                });
            } 

            foreach (var column in sourceTable.Columns)
            {
                table.Columns.Add(column);
            }

            if (sourceTable.Columns.All(c => c.DeltaType != EDeltaType.RejectedReason))
            {
                table.Columns.Add(new TableColumn("RejectReason")
                {
                    DeltaType = EDeltaType.RejectedReason,
                    AllowDbNull = true
                });
            } 

            if (sourceTable.Columns.All(c => c.DeltaType != EDeltaType.ValidationStatus))
            {
                table.Columns.Add(new TableColumn("ValidationStatus")
                {
                    DeltaType = EDeltaType.ValidationStatus,
                    AllowDbNull = true
                });
            } 

            return table;
        }
        
        public override async Task<bool> Open(long auditKey, SelectQuery requestQuery = null, CancellationToken cancellationToken = default)
        {
            AuditKey = auditKey;
            IsOpen = true;
            SelectQuery = requestQuery;
            
            var result = true;

            if (PrimaryTransform != null)
            {
                result = await PrimaryTransform.Open(auditKey, requestQuery, cancellationToken);
                if (!result) return false;
            }

            if (ReferenceTransform != null)
            {
                result = result && await ReferenceTransform.Open(auditKey, null, cancellationToken);
            }

            //store reject column details to improve performance.
            _rejectReasonOrdinal = CacheTable.GetOrdinal(EDeltaType.RejectedReason);
            _operationOrdinal = CacheTable.GetOrdinal(EDeltaType.DatabaseOperation);
            _validationStatusOrdinal = CacheTable.GetOrdinal(EDeltaType.ValidationStatus);

            _primaryFieldCount = PrimaryTransform?.FieldCount ?? 0;
            _columnCount = CacheTable.Columns.Count;
            _mapFieldOrdinals = new List<(int, int)>();

            for (var i = 0; i < _primaryFieldCount; i++)
            {
                _mapFieldOrdinals.Add((GetOrdinal(PrimaryTransform.GetName(i)), _mappingTable.GetOrdinal(PrimaryTransform.GetName(i))));
            }

            return result;
        }

        public override bool RequiresSort => false;

        public override bool ResetTransform()
        {
            _lastRecord = false;
            return true;
        }

        protected override async Task<object[]> ReadRecord(CancellationToken cancellationToken = default)
        {
            //the saved reject row is when a validation outputs two rows (pass & fail).
            if (_savedRejectRow != null)
            {
                var row = _savedRejectRow;
                _savedRejectRow = null;
                return row;
            }

            if (_lastRecord)
            {
                return null;
            }

            while (await PrimaryTransform.ReadAsync(cancellationToken))
            {
                var rejectReason = new StringBuilder();
                var finalInvalidAction = EInvalidAction.Pass;

                //copy row data.
                var passRow = new object[_columnCount];
                for (var i = 0; i < _primaryFieldCount; i++)
                {
                    passRow[_mapFieldOrdinals[i].Item1] = PrimaryTransform[i];
                }

                if (passRow[_operationOrdinal] == null)
                {
                    passRow[_operationOrdinal] = 'C';
                }

                object[] rejectRow = null;

                bool passed;
                bool ignore;

                //run the validation functions
                try
                {
                    (passed, ignore) = await Mappings.ProcessInputData(PrimaryTransform.CurrentRow, cancellationToken);
                }
                catch (FunctionIgnoreRowException)
                {
                    passed = false;
                    ignore = true;
                }
                catch (Exception ex)
                {
                    throw new TransformException(
                        $"The validation transform {Name} failed.  {ex.Message}",
                        ex);
                }

                if (ignore)
                {
                    TransformRowsIgnored += 1;
                }
                else if (!passed)
                {
                    foreach (var mapping in Mappings.OfType<MapValidation>())
                    {
                        if (!mapping.Validated(out var reason))
                        {
                            rejectReason.AppendLine(reason);

                            if (mapping.Function.InvalidAction == EInvalidAction.Abend)
                            {
                                var reason1 = $"The validation rule abended as the invalid action is set to abend.  " + rejectReason;
                                throw new Exception(reason1);
                            }
                            
                            //set the final invalidation action based on priority order of other rejections.
                            finalInvalidAction = finalInvalidAction < mapping.Function.InvalidAction ? mapping.Function.InvalidAction : finalInvalidAction;

                            if (mapping.Function.InvalidAction == EInvalidAction.Reject || mapping.Function.InvalidAction == EInvalidAction.RejectClean)
                            {
                                //if the row is rejected, copy unmodified row to a reject row.
                                if (rejectRow == null)
                                {
                                    rejectRow = new object[CacheTable.Columns.Count];
                                    passRow.CopyTo(rejectRow, 0);
                                    rejectRow[_operationOrdinal] = 'R';
                                    TransformRowsRejected++;
                                }
                            }
                        }
                    }
                }

                if (finalInvalidAction == EInvalidAction.RejectClean ||
                    finalInvalidAction == EInvalidAction.Clean)
                {
                    // update the pass row with any outputs from clean functions.
                    var cleanRow = new object[_columnCount];
                    Mappings.MapOutputRow(cleanRow);
                    
                    //copy row data.
                    for (var i = 0; i < _primaryFieldCount; i++)
                    {
                        passRow[_mapFieldOrdinals[i].Item1] = cleanRow[_mapFieldOrdinals[i].Item2];
                    }

                    if (passRow[_operationOrdinal] == null)
                    {
                        passRow[_operationOrdinal] = 'C';
                    }
                }
   
                if (ValidateDataTypes)
                {
                    for (var i = 1; i < _columnCount; i++)
                    {
                        // value if the position - 1 due to the "Operation" column being in pos[0]
                        var value = passRow[i];
                        var col = CacheTable.Columns[i];

                        if (col.DeltaType == EDeltaType.TrackingField || col.DeltaType == EDeltaType.NonTrackingField)
                        {

                            if (value == null || value is DBNull)
                            {
                                if (col.AllowDbNull == false)
                                {
                                    if (rejectRow == null)
                                    {
                                        rejectRow = new object[_columnCount];
                                        passRow.CopyTo(rejectRow, 0);
                                        rejectRow[_operationOrdinal] = 'R';
                                        TransformRowsRejected++;
                                    }
                                    rejectReason.AppendLine("Column:" + col.Name + ": Tried to insert null into non-null column.");
                                    finalInvalidAction = EInvalidAction.Reject;
                                }
                                passRow[i] = DBNull.Value;
                            }
                            else
                            {
                                try
                                {
                                    passRow[i] =  Operations.Parse(col.DataType, value);

                                    if(col.DataType == ETypeCode.String && col.MaxLength != null)
                                    {
                                        if(((string)passRow[i]).Length > col.MaxLength)
                                        {
                                            throw new DataTypeParseException($"The column {col.Name} value exceeded the maximum string length of {col.MaxLength}.");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // if the parse fails on the column, then write out a reject record.
                                    if (rejectRow == null)
                                    {
                                        rejectRow = new object[_columnCount];
                                        passRow.CopyTo(rejectRow, 0);
                                        rejectRow[_operationOrdinal] = 'R';
                                        TransformRowsRejected++;
                                    }
                                    rejectReason.AppendLine(ex.Message);
                                    finalInvalidAction = EInvalidAction.Reject;
                                }
                            }
                        }
                    }
                }

                switch(finalInvalidAction)
                {
                    case EInvalidAction.Pass:
                        passRow[_validationStatusOrdinal] = "passed";
                        return passRow;
                    case EInvalidAction.Clean:
                        passRow[_validationStatusOrdinal] = "cleaned";
                        return passRow;
                    case EInvalidAction.RejectClean:
                        passRow[_validationStatusOrdinal] = "rejected-cleaned";
                        rejectRow[_validationStatusOrdinal] = "rejected-cleaned";
                        rejectRow[_rejectReasonOrdinal] = rejectReason.ToString();
                        _savedRejectRow = rejectRow;
                        return passRow;
                    case EInvalidAction.Reject:
                        passRow[_validationStatusOrdinal] = "rejected";
                        rejectRow[_validationStatusOrdinal] = "rejected";
                        rejectRow[_rejectReasonOrdinal] = rejectReason.ToString();
                        return rejectRow;
                }

                //should never get here.
                throw new TransformException("Validation failed due to an unknown error.");
            }

            return null;

        }

    }
}
