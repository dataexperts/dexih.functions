﻿using System.Collections.Generic;
using System.Runtime.Serialization;
using Dexih.Utils.DataType;


namespace dexih.functions.Parameter
{
    [DataContract]
    public class ParameterOutputColumn : Parameter
    {
        public ParameterOutputColumn()
        {
        }

        /// <summary>
        /// Initializes are parameter
        /// </summary>
        /// <param name="name">Paramter name</param>
        /// <param name="column"></param>
        public ParameterOutputColumn(
            string name, 
            TableColumn column
        )
        {
            Name = name;
            DataType = column?.DataType ?? ETypeCode.Unknown;
            Rank = column?.Rank ?? 0;
            Column = column;
        }

        public ParameterOutputColumn(
            string name, 
            ETypeCode dataType,
            int rank,
            TableColumn column
        )
        {
            Name = name;
            DataType = dataType;
            Rank = rank;
            Column = column;
        }

        public ParameterOutputColumn(string columName, ETypeCode dataType)
        {
            Name = columName;
            DataType = dataType;
            Column = new TableColumn(columName, dataType);
        }

        [DataMember(Order = 0)]
        public TableColumn Column;

        /// <summary>
        /// The index of the datarow to get the value from.
        /// </summary>
        private int _rowOrdinal;

        public override void InitializeOrdinal(Table table, Table joinTable = null)
        {
            if (Column == null)
            {
                _rowOrdinal = -1;
                return;
            }
            
            _rowOrdinal = table.GetOrdinal(Column);
            if (_rowOrdinal < 0)
            {
                table.Columns.Add(Column);
                _rowOrdinal = table.Columns.Count - 1;
            }
        }

        public override void SetInputData(object[] data, object[] joinRow = null)
        {
            SetValue(data?[_rowOrdinal]);
        }

        public override void PopulateRowData(object value, object[] data, object[] joinRow = null)
        {
            if (_rowOrdinal < 0)
            {
                return;
            }

            SetValue(value);
            data[_rowOrdinal] = Value;
        }
        
        public override Parameter Copy()
        {
            return new ParameterOutputColumn(Name, Column);
        }

        public override IEnumerable<TableColumn> GetOutputColumns()
        {
            yield return Column;
        }


    }
}