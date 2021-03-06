﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dexih.functions;
using dexih.functions.Query;
using dexih.transforms.Exceptions;
using dexih.transforms.View;
using Dexih.Utils.MessageHelpers;
using NetTopologySuite.Geometries;


namespace dexih.transforms
{
    /// <summary>
    /// Writes out a simple json stream containing the headers and data for the reader.
    /// This stream is compacted by placing the column names at the top, and each row containing an array.
    /// </summary>
    public class StreamJsonCompact : Stream
    {
        private const int BufferSize = 50000;
        private readonly DbDataReader _reader;
        private readonly MemoryStream _memoryStream;
        private readonly StreamWriter _streamWriter;
        private long _position;
        private readonly long _maxRows;
        private long _rowCount;
        private bool _hasRows;
        private bool _first;
        private object[] _valuesArray;
        private readonly int _maxFieldSize;
        private readonly SelectQuery _selectQuery;
        private readonly ViewConfig _viewConfig;
        private readonly string _name;
        private readonly bool _showTransformProperties;

        private TableColumn[] _columns = null;

        public StreamJsonCompact(string name, DbDataReader reader, SelectQuery selectQuery = null,
            int maxFieldSize = -1, ViewConfig viewConfig = null, bool showTransformProperties = true)
        {
            _reader = reader;
            _memoryStream = new MemoryStream(BufferSize);
            _streamWriter = new StreamWriter(_memoryStream) {AutoFlush = true};
            _position = 0;
            _name = name;
            _viewConfig = viewConfig;
            _selectQuery = selectQuery;
            _showTransformProperties = showTransformProperties;

            var maxRows = selectQuery?.Rows ?? -1;
            _maxRows = maxRows <= 0 ? long.MaxValue : maxRows;

            _maxFieldSize = maxFieldSize;
            _rowCount = 0;
            _hasRows = true;
            _first = true;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => -1;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException("The position cannot be set.");
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return AsyncHelper.RunSync(() => ReadAsync(buffer, offset, count, CancellationToken.None));
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            try
            {
                if (_first)
                {
                    _streamWriter.Write("{\"name\": \"" + System.Web.HttpUtility.JavaScriptStringEncode(_name) + "\"");

                    if (_viewConfig != null)
                    {
                        _streamWriter.Write(", \"viewConfig\":" + _viewConfig.Serialize());
                    }

                    _streamWriter.Write(", \"columns\": ");

                    // if this is a transform, then use the dataTypes from the cache table
                    if (_reader is Transform transform)
                    {
                        if (!transform.IsOpen)
                        {
                            var openReturn = await transform.Open(_selectQuery, cancellationToken);

                            if (!openReturn)
                            {
                                throw new TransformException("Failed to open the transform.");
                            }
                        }

                        object ColumnObject(IEnumerable<TableColumn> columns)
                        {
                            return columns?.Select(c => new
                            {
                                name = c.Name, logicalName = c.LogicalName, dataType = c.DataType,
                                childColumns = ColumnObject(c.ChildColumns)
                            });
                        }

                        var columnSerializeObject = ColumnObject(transform.CacheTable.Columns).Serialize();

                        if (transform.CacheTable.Columns.Any(c => c.Format != null))
                        {
                            _columns = transform.CacheTable.Columns.ToArray();
                        }
                        
                        _streamWriter.Write(columnSerializeObject);
                    }
                    else
                    {
                        for (var j = 0; j < _reader.FieldCount; j++)
                        {
                            var colName = _reader.GetName(j);
                            _streamWriter.Write(new
                            {
                                name = colName, logicalName = colName, dataType = _reader.GetDataTypeName(j)
                            }.Serialize() + ",");
                        }
                    }

                    _valuesArray = new object[_reader.FieldCount];

                    _streamWriter.Write(", \"data\": [");
                    _memoryStream.Position = 0;
                }

                if (!(_hasRows || _rowCount > _maxRows) && _memoryStream.Position >= _memoryStream.Length)
                {
                    _reader.Close();
                    return 0;
                }

                var readCount = _memoryStream.Read(buffer, offset, count);

                // if the buffer already has enough content.
                if (readCount < count && count > _memoryStream.Length - _memoryStream.Position)
                {
                    _memoryStream.SetLength(0);

                    if (_first)
                    {
                        _hasRows = await _reader.ReadAsync(cancellationToken);

                        if (_hasRows == false)
                        {
                            await _streamWriter.WriteAsync("]");
                            if (_showTransformProperties)
                            {
                                if (_reader is Transform transform)
                                {
                                    if (_showTransformProperties)
                                    {
                                        var properties = transform.GetTransformProperties(true);
                                        var propertiesSerialized = properties.Serialize();
                                        await _streamWriter.WriteAsync(", \"transformProperties\":" + propertiesSerialized);
                                    }

                                    var status = transform.GetTransformStatus();
                                    
                                    if (status != null)
                                    {
                                        await _streamWriter.WriteAsync($", \"status\":" + status.Serialize());
                                    }
                                }
                            }
                            
                            await _streamWriter.WriteAsync("}");
                        }

                        _first = false;
                    }

                    // populate the stream with rows, up to the buffer size.
                    while (_hasRows)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }

                        _reader.GetValues(_valuesArray);

                        for (var i = 0; i < _valuesArray.Length; i++)
                        {
                            if (_valuesArray[i] is byte[])
                            {
                                _valuesArray[i] = "binary data not viewable.";
                            }

                            if (_valuesArray[i] is Geometry geometry)
                            {
                                _valuesArray[i] = geometry.AsText();
                            }

                            if (_valuesArray[i] is string valueString && _maxFieldSize >= 0 &&
                                valueString.Length > _maxFieldSize)
                            {
                                _valuesArray[i] =
                                    valueString.Substring(0, _maxFieldSize) + " (field data truncated)";
                            }

                            if (_columns?[i] != null && _columns[i].Format != null)
                            {
                                var formatted = _columns[i].FormatValue(_valuesArray[i]);
                                if (!Equals(_valuesArray[i], formatted))
                                {
                                    _valuesArray[i] = new {f = _columns[i].FormatValue(_valuesArray[i]), r = _valuesArray[i]};    
                                }
                            }
                        }

                        var row = _valuesArray.Serialize();

                        await _streamWriter.WriteAsync(row);

                        _rowCount++;
                        _hasRows = await _reader.ReadAsync(cancellationToken);

                        if (_hasRows && _rowCount < _maxRows)
                        {
                            await _streamWriter.WriteAsync(",");
                        }
                        else
                        {
                            await _streamWriter.WriteAsync("]");

                            ReturnValue status = null;

                            if (_reader is Transform transform)
                            {
                                if (_showTransformProperties)
                                {
                                    var properties = transform.GetTransformProperties(true);
                                    var propertiesSerialized = properties.Serialize();
                                    await _streamWriter.WriteAsync(", \"transformProperties\":" + propertiesSerialized);
                                }

                                status = transform.GetTransformStatus();
                            }

                            if (_hasRows)
                            {
                                var message = new ReturnValue(false,
                                    $"The rows were truncated to the maximum rows {_maxRows}.\"", null);
                                if (status != null)
                                {
                                    var newStatus = new ReturnValueMultiple();
                                    newStatus.Add(status);
                                    newStatus.Add(message);
                                    status = newStatus;
                                }
                                else
                                {
                                    status = message;
                                }
                            }

                            if (status != null)
                            {
                                await _streamWriter.WriteAsync($", \"status\":" + status.Serialize());
                            }

                            await _streamWriter.WriteAsync("}");

                            _hasRows = false;
                            break;
                        }
                    }

                    _memoryStream.Position = 0;

                    readCount += _memoryStream.Read(buffer, readCount, count - readCount);
                }

                _position += readCount;

                return readCount;
            }
            catch
            {
                try
                {
                    await _reader.CloseAsync();
                } catch {}

                throw;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("The Seek function is not supported.");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("The SetLength function is not supported.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The Write function is not supported.");
        }

        public override void Close()
        {
            _streamWriter?.Close();
            _memoryStream?.Close();
            _reader?.Close();
            base.Close();
        }
    }
}