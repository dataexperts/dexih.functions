﻿using dexih.functions;
using dexih.functions.Query;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dexih.transforms
{
    /// <summary>
    /// A source transform that uses a pre-populated Table as an input.
    /// </summary>
    public sealed class ReaderMemory : Transform
    {
        public Table DataTable { get; set; }

        private SelectQuery _selectQuery;
        private IList<object[]> _data;
        private int _currentRow;
        
        // flag used to indicate if the cache has loaded, so no more records will be loaded 
        // after resets and row positions.
        private bool _cacheLoaded = false;

        #region Constructors

        public ReaderMemory(Table dataTable, List<Sort> sortFields = null)
        {
            CacheTable = new Table(dataTable.Name, dataTable.Columns, new TableCache()) {OutputSortFields = sortFields};

            DataTable = dataTable;
            _data = dataTable.Data;
            
            Reset();

            IsOpen = true;
            SortFields = sortFields;
        }
        
        public override Task<bool> Open(long auditKey, SelectQuery selectQuery = null, CancellationToken cancellationToken = default)
        {
            IsOpen = true;
            _selectQuery = selectQuery;

//            if (_selectQuery?.Filters?.Count > 0)
//            {
//                _data = _dataTable.LookupMultipleRows(_selectQuery.Filters);
//            }
//            else
//            {
//                _data = _dataTable.Data;
//            }

            _data = DataTable.Data;

            return Task.FromResult(true);
        }

        public override List<Sort> SortFields { get; }

//        public void Add(object[] values)
//        {
//            CacheTable.AddRow(values);
//        }

        #endregion

        public override string TransformName { get; } = "Memory Reader";
        public override string TransformDetails => CacheTable?.Name ?? "Unknown";

        public override bool ResetTransform()
        {
            _currentRow = -1;
            return true;
        }

        protected override Task<object[]> ReadRecord(CancellationToken cancellationToken = default)
        {
            if (_data == null)
            {
                return Task.FromResult<object[]>(null);
            }

            _currentRow++;
            while(!_cacheLoaded && _currentRow < _data.Count)
            {
                var row = _data[_currentRow];
                var filtered = _selectQuery?.EvaluateRowFilter(row, CacheTable)?? true;
                if(!filtered)
                {
                    _currentRow++;
                    continue;
                }
                return Task.FromResult(row);
            }

            if (CacheMethod != ECacheMethod.NoCache)
            {
                _cacheLoaded = true;
            }

            return Task.FromResult<object[]>(null);
        }

        public override bool IsClosed => _currentRow >= _data.Count;
        public override bool HasRows => _currentRow < _data.Count && _data.Count > 0;
        
        public override Task<bool> InitializeLookup(long auditKey, SelectQuery query, CancellationToken cancellationToken = default)
        {
            Reset(true);
            _cacheLoaded = false;
            _currentRow = -1;
            return Open(auditKey, query, cancellationToken);
        }
    }
}
