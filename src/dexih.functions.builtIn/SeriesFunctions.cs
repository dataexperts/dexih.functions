﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using dexih.functions.Exceptions;
using Dexih.Utils.DataType;

namespace dexih.functions.BuiltIn
{
    public class SeriesValue<T>
    {
        public SeriesValue(DateTime series, T value, EAggregate aggregate)
        {
            Series = series;
            Value = value;
            Count = 1;
            Aggregate = aggregate;
        }
        
        public DateTime Series { get; set; }
        public T Value { get; set; }
        public int Count { get; set; }
        public EAggregate Aggregate { get; set; }

        public void AddValue(T value)
        {
            Count++;
            
            switch (Aggregate)
            {
                case EAggregate.Sum:
                case EAggregate.Average:
                    Value = Operations.Add(Value, value);
                    break;
                case EAggregate.Min:
                    if (Operations.LessThan(value, Value))
                    {
                        Value = value;
                    }
                    break;
                case EAggregate.Max:
                    if (Operations.GreaterThan(value, Value))
                    {
                        Value = value;
                    }
                    break;
                case EAggregate.Count:
                    break;
                case EAggregate.First:
                    break;
                case EAggregate.Last:
                    Value = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Aggregate), Aggregate, null);
            }
        }

        public T Result()
        {
            if (Aggregate == EAggregate.Average)
            {
                return Operations.DivideInt(Value, Count);
            }

            return Value;
        }
    }

    public class SeriesResult<T>
    {
        public DateTime SeriesItem { get; set; }
        public T Value { get; set; }
        public int CountBack { get; set; }
    }

    public class PreviousSeriesResult<T>
    {
        public DateTime SeriesItem { get; set; }
        public T Value { get; set; }
    }
    
    public class SeriesFunctions<T>
    {
        //The cache parameters are used by the functions to maintain a state during a transform process.
        private  OrderedDictionary _cacheSeries;
        
        public bool Reset()
        {
            _cacheSeries?.Clear();
            return true;
        }

        private void AddSeries(DateTime series, T value, EAggregate duplicateAggregate)
        {
            if (_cacheSeries == null)
            {
                _cacheSeries = new OrderedDictionary();
            }

            if (_cacheSeries.Contains(series))
            {
                var current = (SeriesValue<T>) _cacheSeries[series];
                current.AddValue(value);
            }
            else
            {
                _cacheSeries.Add(series, new SeriesValue<T>(series, value, duplicateAggregate));
            }
        }
        
        [TransformFunction(FunctionType = EFunctionType.Series, Category = "Series", Name = "Moving Series Average", Description = "Calculates moving series average of the last (pre-count) points and the future (post-count) points.", ResultMethod = nameof(MovingAverageResult), ResetMethod = nameof(Reset), GenericType = EGenericType.Numeric)]
        public void MovingSeriesAverage([TransformFunctionVariable(EFunctionVariable.SeriesValue)]DateTime series, T value, EAggregate duplicateAggregate = EAggregate.Sum)
        {
            AddSeries(series, value, duplicateAggregate);
        }

        public T MovingAverageResult([TransformFunctionVariable(EFunctionVariable.Index)]int index, int preCount, int postCount)
        {
            var lowIndex = index < preCount ? 0 : index - preCount;
            var valueCount = _cacheSeries.Count;
            var highIndex = postCount + index + 1;
            if (highIndex > valueCount) highIndex = valueCount;

            T sum = default;
            var denominator = highIndex - lowIndex;

            for (var i = lowIndex; i < highIndex; i++)
            {
                var value = (SeriesValue<T>) _cacheSeries[i];
                sum = Operations.Add(sum, value.Result());
            }
            
            //return the result.
            if (denominator == 0)
            {
                return default;
            }

            return Operations.DivideInt(sum, denominator);
        }
        
        [TransformFunction(FunctionType = EFunctionType.Series, Category = "Series", Name = "Highest Value Since ", Description = "Return the last period that had a higher value than this.", ResultMethod = nameof(HighestSinceResult), ResetMethod = nameof(Reset), GenericType = EGenericType.Numeric)]
        public void HighestSince([TransformFunctionVariable(EFunctionVariable.SeriesValue)]DateTime series, T value, EAggregate duplicateAggregate = EAggregate.Sum)
        {
            AddSeries(series, value, duplicateAggregate);
        }

        public SeriesResult<T> HighestSinceResult([TransformFunctionVariable(EFunctionVariable.Index)]int index)
        {
            var i = index - 1;
            var currentSeries = (SeriesValue<T>) _cacheSeries[index];
            var currentValue = currentSeries.Result();
            while (i > 0)
            {
                var checkValue = ((SeriesValue<T>)_cacheSeries[i]).Result();
                if (Operations.GreaterThan(checkValue, currentValue))
                {
                    return new SeriesResult<T>()
                    {
                        CountBack = index - i,
                        Value = checkValue,
                        SeriesItem = ((SeriesValue<T>) _cacheSeries[i]).Series
                    };
                }
                i--;
            }

            // if not value found, the current value is the highest.
            return new SeriesResult<T>()
            {
                CountBack = 0,
                Value = currentValue,
                SeriesItem = currentSeries.Series
            };
        }
        
        [TransformFunction(FunctionType = EFunctionType.Series, Category = "Series", Name = "Highest Value Since ", Description = "Return the last period that had a lower value than this.", ResultMethod = nameof(LowestSinceResult), ResetMethod = nameof(Reset), GenericType = EGenericType.Numeric)]
        public void LowestSince([TransformFunctionVariable(EFunctionVariable.SeriesValue)]DateTime series, T value, EAggregate duplicateAggregate = EAggregate.Sum)
        {
            AddSeries(series, value, duplicateAggregate);
        }

        public SeriesResult<T> LowestSinceResult([TransformFunctionVariable(EFunctionVariable.Index)]int index)
        {
            var i = index - 1;
            var currentSeries = (SeriesValue<T>) _cacheSeries[index];
            var currentValue = currentSeries.Result();
            while (i > 0)
            {
                var checkValue = ((SeriesValue<T>)_cacheSeries[i]).Result();
                if (Operations.LessThan(checkValue, currentValue))
                {
                    return new SeriesResult<T>()
                    {
                        CountBack = index - i,
                        Value = checkValue,
                        SeriesItem = ((SeriesValue<T>) _cacheSeries[i]).Series
                    };
                }
                i--;
            }
            
            // if not value found, the current value is the lowest.
            return new SeriesResult<T>()
            {
                CountBack = 0,
                Value = currentValue,
                SeriesItem = currentSeries.Series
            };
        }
        
        [TransformFunction(FunctionType = EFunctionType.Series, Category = "Series", Name = "Previous Series Value", Description = "Return the value from the series item (count periods ago).", ResultMethod = nameof(PreviousSeriesValueResult), ResetMethod = nameof(Reset), GenericType = EGenericType.All)]
        public void PreviousSeriesValue([TransformFunctionVariable(EFunctionVariable.SeriesValue)]DateTime series, T value, EAggregate duplicateAggregate = EAggregate.Sum)
        {
            AddSeries(series, value, duplicateAggregate);
        }

        public PreviousSeriesResult<T> PreviousSeriesValueResult([TransformFunctionVariable(EFunctionVariable.Index)]int index, int count = 1)
        {
            if (index < count || _cacheSeries.Count > index - count)
            {
                return default;
            }
            
            var value = (SeriesValue<T>)_cacheSeries[index-count];
            return new PreviousSeriesResult<T>()
            {
                Value = value.Result(),
                SeriesItem = value.Series
            };
            
        }
        
        [TransformFunction(FunctionType = EFunctionType.Series, Category = "Series", Name = "Previous Series Value If Null", Description = "Return the previous series item (count periods ago) if the current value is null.", ResultMethod = nameof(PreviousSeriesValueNullResult), ResetMethod = nameof(Reset), GenericType = EGenericType.All, GenericTypeDefault = ETypeCode.Decimal)]
        public void PreviousSeriesValueNull([TransformFunctionVariable(EFunctionVariable.SeriesValue)]DateTime series, T value, EAggregate duplicateAggregate = EAggregate.Sum)
        {
            AddSeries(series, value, duplicateAggregate);
        }

        public PreviousSeriesResult<T> PreviousSeriesValueNullResult([TransformFunctionVariable(EFunctionVariable.Index)]int index)
        {
            var i = index - 1;
            
            if (_cacheSeries.Count == 0)
            {
                throw new FunctionException("Cannot get the previous value as there are no rows processed.");    
            }

            var currentSeries = (SeriesValue<T>) _cacheSeries[index];
            var currentValue = currentSeries.Result();

            if (!EqualityComparer<T>.Default.Equals(currentValue, default(T)))
            {
                return new PreviousSeriesResult<T>()
                {
                    Value = currentValue,
                    SeriesItem = currentSeries.Series
                };
            }
            
            while (i >= 0)
            {
                var checkValue = ((SeriesValue<T>)_cacheSeries[i]).Result();
                if (!EqualityComparer<T>.Default.Equals(checkValue, default(T)))
                {
                    return new PreviousSeriesResult<T>()
                    {
                        Value = checkValue,
                        SeriesItem = ((SeriesValue<T>) _cacheSeries[i]).Series
                    };
                }
                i--;
            }

            // if not value found, the current value is the highest.
            return new PreviousSeriesResult<T>()
            {
                Value = currentValue,
                SeriesItem = currentSeries.Series
            };
            
        }
    }
}