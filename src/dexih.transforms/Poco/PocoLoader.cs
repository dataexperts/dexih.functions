﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace dexih.transforms
{

    
    public class PocoLoader<T> : IEnumerable<T>
    {
        private IPocoEnumerator<T> _enumerator;

        public async Task<List<T>> ToListAsync(DbDataReader reader)
        {
            return await ToListAsync(reader, CancellationToken.None);
        }

        public async Task<List<T>> ToListAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            var pocoMapping = new PocoMapper<T>(reader);
            var data = new List<T>();

            while (await reader.ReadAsync(cancellationToken))
            {
                data.Add(pocoMapping.GetItem());
            }

            return data;
        }

        public void Open(DbDataReader reader)
        {
            _enumerator = new PocoEnumerator<T>(reader);
        }

        public void OpenCached(DbDataReader reader)
        {
            _enumerator = new PocoCachedEnumerator<T>(reader);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _enumerator;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public async Task ReadToEndAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && await _enumerator.MoveNextAsync(cancellationToken))
            {
            }
        }
        
        public T this[int index] =>  _enumerator[index];

        public int Count => _enumerator.Count;
    }

    
}