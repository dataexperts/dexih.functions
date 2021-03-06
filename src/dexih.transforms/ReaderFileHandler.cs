﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using dexih.functions;
using System.Threading;
using dexih.functions.Query;
using dexih.transforms.Exceptions;
using dexih.transforms.File;

namespace dexih.transforms
{
    public sealed class ReaderFileHandler : Transform
    {
        private readonly FileHandlerBase _fileHandler;

		public FlatFile CacheFlatFile => (FlatFile)CacheTable;

        public ReaderFileHandler(FileHandlerBase fileHandler, Table table)
        {
            CacheTable = table;
            _fileHandler = fileHandler;
        }
        
        protected override Task CloseConnections()
        {
            _fileHandler?.Dispose();
            return Task.CompletedTask;
        }

        public override Task<bool> Open(long auditKey, SelectQuery requestQuery = null, CancellationToken cancellationToken = default)
        {
            if (IsOpen)
            {
                throw new ConnectionException("The file reader connection is already open.");
            }
            
            AuditKey = auditKey;
            IsOpen = true;
            SelectQuery = requestQuery;
            return Task.FromResult(true);
        }

        public override string TransformName => $"File Reader: {_fileHandler?.FileType}";

        public override Dictionary<string, object> TransformProperties()
        {
            return new Dictionary<string, object>()
            {
                {"FileType", _fileHandler?.FileType??"Unknown"},
            };
        }

        public override bool ResetTransform()
        {
            return IsOpen;
        }

        protected override Task<object[]> ReadRecord(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                try
                {
                    return _fileHandler.GetRow(new FileProperties());
                }
                catch (Exception ex)
                {
                    throw new ConnectionException("The flat file reader failed with the following message: " + ex.Message, ex);
                }
            }

        }
        
        public override Task<bool> InitializeLookup(long auditKey, SelectQuery query, CancellationToken cancellationToken = default)
        {
            Reset();
            return Open(auditKey, query, cancellationToken);
        }

        public override bool FinalizeLookup()
        {
            Close();
            return true;
        }


    }
}
