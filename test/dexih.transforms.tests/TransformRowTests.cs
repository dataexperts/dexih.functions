using System;
using System.Collections.Generic;
using System.Linq;
using dexih.functions;
using dexih.functions.BuiltIn;
using dexih.functions.Mappings;
using dexih.functions.Parameter;
using Dexih.Utils.DataType;
using Xunit;

namespace dexih.transforms.tests
{
    public class TransformRowTests
    {

        [Fact]
        public async void RowTest_CommaSeparated()
        {
            var table = new Table("test", 0, new TableColumns()
            {
                new TableColumn("csvField", DataType.ETypeCode.String)
            });

            var values = new[] {"a", "b", "c", "d", "e"};

            table.AddRow(string.Join(',', values));
            var source = new ReaderMemory(table);
            source.Reset();
            
            var mappings = new Mappings(false, true);

            var split = Functions.GetFunction(typeof(RowFunctions).FullName, nameof(RowFunctions.SplitColumnToRows)).GetTransformFunction();
            
            var parameters = new Parameters()
            {
                Inputs = new Parameter[]
                {
                    new ParameterValue("separator", DataType.ETypeCode.String, ","),
                    new ParameterColumn("csvField", DataType.ETypeCode.String),
                    new ParameterValue("rows", DataType.ETypeCode.Int32, 4),
                },
                Outputs = new Parameter[] 
                {
                    new ParameterOutputColumn("Value", DataType.ETypeCode.String)
                }
            };
            
            mappings.Add(new MapFunction(split, parameters));
            
            var transformRow = new TransformRows(source, mappings);

            var pos = 0;
            while (await transformRow.ReadAsync())
            {
                Assert.Equal(values[pos++], transformRow["Value"]);
            }
            
            Assert.Equal(4, pos);
        }
        
        [Fact]
        public async void RowTest_ColumnPivot()
        {
            var table = new Table("test", 0, new TableColumns()
            {
                new TableColumn("col0", DataType.ETypeCode.String),
                new TableColumn("col1", DataType.ETypeCode.String),
                new TableColumn("col2", DataType.ETypeCode.String),
                new TableColumn("col3", DataType.ETypeCode.String)
            });

            var values = new object[] {"a", "b", "c", "d"};

            table.AddRow(values);
            var source = new ReaderMemory(table);
            source.Reset();
            
            var mappings = new Mappings(false, true);

            var parameters = new Parameters()
            {
                Inputs = new Parameter[]
                {
                    new ParameterArray("columns", DataType.ETypeCode.String, new List<Parameter>()
                    {
                        new ParameterColumn("col0", DataType.ETypeCode.String),
                        new ParameterColumn("col1", DataType.ETypeCode.String),
                        new ParameterColumn("col2", DataType.ETypeCode.String),
                        new ParameterColumn("col3", DataType.ETypeCode.String),
                    }), 
                },
                Outputs = new Parameter[] 
                {
                    new ParameterOutputColumn("column", DataType.ETypeCode.String),
                    new ParameterOutputColumn("value", DataType.ETypeCode.String),
                }
            };

            var function = Functions.GetFunction(typeof(RowFunctions).FullName, nameof(RowFunctions.ColumnsToRows)).GetTransformFunction(parameters);

            mappings.Add(new MapFunction(function, parameters));
            
            var transformRow = new TransformRows(source, mappings);

            var pos = 0;
            while (await transformRow.ReadAsync())
            {
                Assert.Equal($"col{pos}", transformRow["column"]);
                Assert.Equal(values[pos], transformRow["value"]);
                pos++;
            }
            
            Assert.Equal(4, pos);
        }
    }
}