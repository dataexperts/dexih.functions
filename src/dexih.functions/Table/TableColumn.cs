﻿using System;
using Newtonsoft.Json;
using static Dexih.Utils.DataType.DataType;
using Dexih.Utils.CopyProperties;
using Newtonsoft.Json.Converters;

namespace dexih.functions
{
    [Serializable]
    public class TableColumn : IEquatable<TableColumn>

    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum EDeltaType
        {
            AutoIncrement, // column is auto incremented by the dexih
            DbAutoIncrement, // column is auto incremented by the database
            SourceSurrogateKey,
            ValidFromDate,
            ValidToDate,
            CreateDate,
            UpdateDate,
            CreateAuditKey,
            UpdateAuditKey,
            IsCurrentField,
            Version,
            NaturalKey,
            TrackingField,
            NonTrackingField,
            IgnoreField,
            ValidationStatus,
            RejectedReason,
            FileName,
            FileRowNumber,
            AzureRowKey, //special column type for Azure Storage Tables.  
            AzurePartitionKey, //special column type for Azure Storage Tables.  
            TimeStamp, //column that is generated by the database.
            DatabaseOperation, // C/U/D/T/R (Create/Update/Delete/Truncate/Reject)
            ResponseSuccess, // webservice/function response aws successful
            ResponseData, // raw data from a webservice/function response
            ResponseStatus, // status code from a webservice/function call
            ResponseSegment, // segment of data (such as xpath result) from a response data.
            Error, // error message 
            Url // the full url called for the web service.
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum ESecurityFlag
        {
            None,
            FastEncrypt,
            FastDecrypt,
            FastEncrypted,
            StrongEncrypt,
            StrongDecrypt,
            StrongEncrypted,
            OneWayHash,
            OnWayHashed,
            Hide
        }

        public TableColumn()
        {
//            ExtendedProperties = new Dictionary<string, string>();
        }

        public TableColumn(string columnName, ETypeCode dataType = ETypeCode.String,
            EDeltaType deltaType = EDeltaType.TrackingField, int rank = 0, string parentTable = null)
        {
            Name = columnName;
            LogicalName = columnName;
            DataType = dataType;
            DeltaType = deltaType;
            ReferenceTable = parentTable;
            Rank = rank;
        }

        public TableColumn(string columnName, EDeltaType deltaType, string parentTable = null)
        {
            Name = columnName;
            LogicalName = columnName;
            DataType = GetDeltaDataType(deltaType);
            DeltaType = deltaType;
            ReferenceTable = parentTable;
        }

        public string ReferenceTable { get; set; }

        public string Name { get; set; }

        public string LogicalName { get; set; }

        public string Description { get; set; }

        public ETypeCode DataType
        {
            get
            {
                if (SecurityFlag == ESecurityFlag.None || SecurityFlag == ESecurityFlag.FastDecrypt ||
                    SecurityFlag == ESecurityFlag.StrongDecrypt)
                    return BaseDataType;
                return ETypeCode.String;
            }
            set => BaseDataType = value;
        }

        public int? MaxLength
        {
            get
            {
                if (SecurityFlag == ESecurityFlag.None || SecurityFlag == ESecurityFlag.FastDecrypt ||
                    SecurityFlag == ESecurityFlag.StrongDecrypt)
                    return BaseMaxLength;
                return 250;
            }
            set => BaseMaxLength = value;
        }
        
        /// <summary>
        /// A string that can be used to group columns.  This is also used to structure json/xml data.  Uses syntax group1.subgroup2.subsubgroup3 etc.
        /// </summary>
        public string ColumnGroup { get; set; }

        public int Rank { get; set; } = 0;

        public bool IsArray() => Rank > 0;

        //this is the underlying datatype of a non encrypted data type.  
        public ETypeCode BaseDataType { get; set; }

        //this is the max length of the non-encrypted data type.
        public int? BaseMaxLength { get; set; }

        public int? Precision { get; set; }

        public int? Scale { get; set; }

        public bool AllowDbNull { get; set; }

        public EDeltaType DeltaType { get; set; }

        public bool? IsUnicode { get; set; }

        public object DefaultValue { get; set; }

        public bool IsUnique { get; set; }

        public bool IsMandatory { get; set; }

        public ESecurityFlag SecurityFlag { get; set; } = ESecurityFlag.None;

        public bool IsInput { get; set; }

        public bool IsIncrementalUpdate { get; set; }
        
        // used by the passthrough to indicate if the column is a part of the parent node, or part of current node.
        public bool IsParent { get; set; } = false;

        public TableColumns ChildColumns { get; set; }

        public bool IsAutoIncrement() => DeltaType == EDeltaType.DbAutoIncrement || DeltaType == EDeltaType.AutoIncrement;

        
        [JsonIgnore, CopyIgnore]
        public Type ColumnGetType
        {
            get => Dexih.Utils.DataType.DataType.GetType(DataType);
            set => DataType = GetTypeCode(value, out _);
        }

        /// <summary>
        /// Returns a string with the schema.columngroup.columnname
        /// </summary>
        /// <returns></returns>
        public string TableColumnName()
        {
            var columnName = (string.IsNullOrEmpty(ReferenceTable) ? "" : ReferenceTable + ".") + (string.IsNullOrEmpty(ColumnGroup) ? "" : ColumnGroup + ".") + Name;
            return columnName;
        }

        /// <summary>
        /// Is the column one form the source (vs. a value added column).
        /// </summary>
        /// <returns></returns>
        public bool IsSourceColumn()
        {
            switch (DeltaType)
            {
                case EDeltaType.NaturalKey:
                case EDeltaType.TrackingField:
                case EDeltaType.NonTrackingField:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Columns which require no mapping and are generated automatically for auditing.
        /// </summary>
        /// <returns></returns>
        public bool IsGeneratedColumn()
        {
            switch (DeltaType)
            {
                case EDeltaType.CreateAuditKey:
                case EDeltaType.UpdateAuditKey:
                case EDeltaType.CreateDate:
                case EDeltaType.UpdateDate:
                case EDeltaType.AutoIncrement:
                case EDeltaType.DbAutoIncrement:
                case EDeltaType.ValidationStatus:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Columns which indicate if the record is current.  These are the createdate, updatedate, iscurrentfield
        /// </summary>
        /// <returns></returns>
        public bool IsCurrentColumn()
        {
            switch (DeltaType)
            {
                case EDeltaType.ValidFromDate:
                case EDeltaType.ValidToDate:
                case EDeltaType.IsCurrentField:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a copy of the column which can be used when generating other tables.
        /// </summary>
        /// <returns></returns>
        public TableColumn Copy(bool includeChildColumns = true)
        {
            var newColumn = new TableColumn
            {
                ReferenceTable = ReferenceTable,
                Name = Name,
                LogicalName = LogicalName,
                Description = Description,
                ColumnGroup = ColumnGroup,
                DataType = DataType,
                MaxLength = MaxLength,
                Precision = Precision,
                Scale = Scale,
                AllowDbNull = AllowDbNull,
                DefaultValue = DefaultValue,
                DeltaType = DeltaType,
                IsUnique = IsUnique,
                IsInput = IsInput,
                IsMandatory = IsMandatory,
                IsParent = IsParent,
                IsIncrementalUpdate = IsIncrementalUpdate,
                Rank = Rank,
            };

            if (includeChildColumns && ChildColumns != null && ChildColumns.Count > 0)
            {
                newColumn.ChildColumns = new TableColumns();

                foreach (var col in ChildColumns)
                {
                    newColumn.ChildColumns.Add(col.Copy());
                }
            }

            switch (SecurityFlag)
            {
                case ESecurityFlag.FastEncrypt:
                    newColumn.SecurityFlag = ESecurityFlag.FastEncrypted;
                    break;
                case ESecurityFlag.StrongEncrypt:
                    newColumn.SecurityFlag = ESecurityFlag.StrongEncrypted;
                    break;
                case ESecurityFlag.OneWayHash:
                    newColumn.SecurityFlag = ESecurityFlag.OnWayHashed;
                    break;
                default:
                    newColumn.SecurityFlag = SecurityFlag;
                    break;
            }

            return newColumn;
        }

        /// <summary>
        /// Gets the default datatype for specified delta column
        /// </summary>
        /// <returns>The delta data type.</returns>
        /// <param name="deltaType">Delta type.</param>
        public static ETypeCode GetDeltaDataType(EDeltaType deltaType)
        {
            switch (deltaType)
            {
                case EDeltaType.AutoIncrement:
                case EDeltaType.SourceSurrogateKey:
                case EDeltaType.CreateAuditKey:
                case EDeltaType.UpdateAuditKey:
                case EDeltaType.FileRowNumber:
                    return ETypeCode.UInt64;
                case EDeltaType.ValidFromDate:
                case EDeltaType.ValidToDate:
                case EDeltaType.CreateDate:
                case EDeltaType.UpdateDate:
                case EDeltaType.TimeStamp:
                    return ETypeCode.DateTime;
                case EDeltaType.IsCurrentField:
                    return ETypeCode.Boolean;
                case EDeltaType.NaturalKey:
                case EDeltaType.TrackingField:
                case EDeltaType.NonTrackingField:
                case EDeltaType.IgnoreField:
                case EDeltaType.ValidationStatus:
                case EDeltaType.RejectedReason:
                case EDeltaType.FileName:
                case EDeltaType.AzureRowKey:
                case EDeltaType.AzurePartitionKey:
                case EDeltaType.DatabaseOperation:
                case EDeltaType.ResponseSuccess:
                case EDeltaType.ResponseData:
                case EDeltaType.ResponseStatus:
                case EDeltaType.ResponseSegment:
                case EDeltaType.Error:
                case EDeltaType.Url:
                    return ETypeCode.String;
                case EDeltaType.DbAutoIncrement:
                    break;
                case EDeltaType.Version:
                    return ETypeCode.Int32;
                default:
                    throw new ArgumentOutOfRangeException(nameof(deltaType), deltaType, null);
            }

            return ETypeCode.String;
        }

        /// <summary>
        /// Compare the column 
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Compare(TableColumn other)
        {
            if (other == null)
            {
                return false;
            }

            return TableColumnName() == other.TableColumnName() ||
                   Name == other.Name;
        }

        public bool Equals(TableColumn other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return
                string.Equals(ReferenceTable, other.ReferenceTable) &&
                string.Equals(Name, other.Name) &&
                string.Equals(LogicalName, other.LogicalName) &&
                string.Equals(Description, other.Description) &&
                Rank == other.Rank &&
                BaseDataType == other.BaseDataType &&
                BaseMaxLength == other.BaseMaxLength &&
                Precision == other.Precision &&
                Scale == other.Scale &&
                AllowDbNull == other.AllowDbNull &&
                DeltaType == other.DeltaType &&
                IsUnicode == other.IsUnicode &&
                Equals(DefaultValue, other.DefaultValue) &&
                IsUnique == other.IsUnique &&
                IsMandatory == other.IsMandatory &&
                SecurityFlag == other.SecurityFlag &&
                IsInput == other.IsInput &&
                IsIncrementalUpdate == other.IsIncrementalUpdate;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TableColumn) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (ReferenceTable != null ? ReferenceTable.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (LogicalName != null ? LogicalName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Description != null ? Description.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Rank;
                hashCode = (hashCode * 397) ^ (int) BaseDataType;
                hashCode = (hashCode * 397) ^ BaseMaxLength.GetHashCode();
                hashCode = (hashCode * 397) ^ Precision.GetHashCode();
                hashCode = (hashCode * 397) ^ Scale.GetHashCode();
                hashCode = (hashCode * 397) ^ AllowDbNull.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) DeltaType;
                hashCode = (hashCode * 397) ^ IsUnicode.GetHashCode();
                hashCode = (hashCode * 397) ^ (DefaultValue != null ? DefaultValue.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ IsUnique.GetHashCode();
                hashCode = (hashCode * 397) ^ IsMandatory.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) SecurityFlag;
                hashCode = (hashCode * 397) ^ IsInput.GetHashCode();
                hashCode = (hashCode * 397) ^ IsIncrementalUpdate.GetHashCode();
                return hashCode;
            }
        }
    }
}