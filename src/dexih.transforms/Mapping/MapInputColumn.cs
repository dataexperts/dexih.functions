﻿using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dexih.functions;

namespace dexih.transforms.Mapping
{
    public class MapInputColumn: Mapping
    {
        public MapInputColumn() {}

        public MapInputColumn(TableColumn inputColumn)
        {
            InputColumn = inputColumn;
            InputValue = inputColumn.DefaultValue;
        }


        public readonly TableColumn InputColumn;
        public object InputValue;

        protected int InputOrdinal = -1;
        protected int OutputOrdinal = -1;

        protected object[] RowData;

        public override void InitializeColumns(Table table, Table joinTable = null, Mappings mappings = null)
        {
            if (InputColumn == null) return;
            
            InputOrdinal = table.GetOrdinal(InputColumn);
        }

        public override void AddOutputColumns(Table table)
        {
            OutputOrdinal = AddOutputColumn(table, InputColumn);
        }

        public override Task<bool> ProcessInputRow(FunctionVariables functionVariables, object[] row, object[] joinRow, CancellationToken cancellationToken)
        {
            RowData = row;
            return Task.FromResult(true);
        }

        public override void MapOutputRow(object[] data)
        {
            data[OutputOrdinal] = InputValue;
        }

        public override object GetOutputValue(object[] row = null)
        {
            return InputValue;
        }

        public override string Description()
        {
            return $"Input ({InputColumn?.Name}";
        }

        public override void Reset(EFunctionType functionType)
        {
        }

        public override IEnumerable<TableColumn> GetRequiredColumns()
        {
            if (InputColumn == null)
            {
                return new TableColumn[0];
            }

            return new[] {InputColumn};
        }

        public void SetInput(IEnumerable<TableColumn> inputColumns)
        {
            var column = inputColumns.SingleOrDefault(c => c.Name == InputColumn.Name);
            if (column != null)
            {
                InputValue = column.DefaultValue;
            }
        }

    }
}