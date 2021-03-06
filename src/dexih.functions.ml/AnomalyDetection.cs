using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;

namespace dexih.functions.ml
{
    public class AnomalyDetection
    {
        //The cache parameters are used by the functions to maintain a state during a transform process.
        private OrderedDictionary _cacheSeries;

        private AnomalyPrediction[] _predictions;

        private PredictionEngine<AnomalyEntry, AnomalyPrediction> _predictionEngine;

        public bool Reset()
        {
            _cacheSeries?.Clear();
            return true;
        }
        
        public string[] ImportModelLabels(byte[] model) => Helpers.ImportModelLabels(model);

        private void AddSeries(object series, float value, EAggregate duplicateAggregate)
        {
            if (_cacheSeries == null)
            {
                _cacheSeries = new OrderedDictionary();
            }

            if (_cacheSeries.Contains(series))
            {
                var current = (SeriesValue<float>) _cacheSeries[series];
                current.AddValue(value);
            }
            else
            {
                _cacheSeries.Add(series, new SeriesValue<float>(series, value, duplicateAggregate));
            }
        }

        private IEnumerable<AnomalyEntry> SeriesValues()
        {
            var values = _cacheSeries.Values.Cast<SeriesValue<float>>().Select(c => new AnomalyEntry() {Value = c.Result});
            return values;
        }

        public class AnomalyEntry
        {
            public float Value;
        }

        public class AnomalyPrediction
        {
            [TransformFunctionParameter]
            [VectorType(3)]
            public double[] Prediction { get; set; }

            [TransformFunctionParameter(Description = "A spike alert for a given data point.")]
            public double Alert => Prediction[0];

            [TransformFunctionParameter(Description = "The value for a given data point in the dataset.")]
            public double Score => Prediction[1];
            
            [TransformFunctionParameter(Description = "The probability indicating how likely this dat point is an anomaly.")]
            public double pValue => Prediction[2];
        }
        
        [TransformFunction(FunctionType = EFunctionType.Series, Category = "Machine Learning", Name = "Anomaly Detection", Description = "Detects spikes in a series of data.", ResultMethod = nameof(SpikeDetectionResult), ResetMethod = nameof(Reset))]
        public void SpikeDetection(
            [TransformFunctionVariable(EFunctionVariable.SeriesValue)]object series, 
            float value, 
            EAggregate duplicateAggregate = EAggregate.Sum
            )
        {
            AddSeries(series, value, duplicateAggregate);
        }
        
        public AnomalyPrediction SpikeDetectionResult(
            [TransformFunctionVariable(EFunctionVariable.Index)]int index,
            [TransformFunctionParameter(Description = "The confidence for spike detection in the range [0, 100]." )] int confidence = 95, 
            [TransformFunctionParameter(Description = "The size of the sliding window for computing the p-value." )] int? pvalueHistoryLength = null,
            [TransformFunctionParameter(Description = "Detect positive or negative anomalies, or both." )] AnomalySide side = AnomalySide.TwoSided
            )
        {
            if (_predictions == null)
            {
                // Create a new context for ML.NET operations. It can be used for exception tracking and logging,
                // as a catalog of available operations and as the source of randomness.
                var mlContext = new MLContext();

                var iidSpikeEstimator = mlContext.Transforms.DetectIidSpike(outputColumnName:
                    nameof(AnomalyPrediction.Prediction), inputColumnName:
                    nameof(AnomalyEntry.Value), confidence:
                    confidence, pvalueHistoryLength:
                    pvalueHistoryLength ?? _cacheSeries.Count / 4,
                    side);

                var trainData = mlContext.Data.LoadFromEnumerable(SeriesValues());

                var trainedModel = iidSpikeEstimator.Fit(trainData);

                var transformedData = trainedModel.Transform(trainData);

                _predictions = mlContext.Data
                    .CreateEnumerable<AnomalyPrediction>(transformedData, reuseRowObject: false).ToArray();
            }

            if (_predictions != null && _predictions.Length > index)
            {
                return _predictions[index];
            }

            return null;
        }
        
        [TransformFunction(FunctionType = EFunctionType.Aggregate, Category = "Machine Learning", Name = "Anomaly Detection", Description = "Generated a model for detecting spikes in a series of data.", ResultMethod = nameof(SpikeDetectionModelResult), ResetMethod = nameof(Reset))]
        public void SpikeDetectionModel(
            [TransformFunctionVariable(EFunctionVariable.SeriesValue)]object series, 
            float value, 
            EAggregate duplicateAggregate = EAggregate.Sum
            )
        {
            AddSeries(series, value, duplicateAggregate);
        }

        [TransformFunctionParameter(Name = "Model", Description = "The model generated by the recommendation engine.")]
        public byte[] SpikeDetectionModelResult(
            [TransformFunctionParameter(Description = "The confidence for spike detection in the range [0, 100].")]
            int confidence = 95,
            [TransformFunctionParameter(Description = "The size of the sliding window for computing the p-value.")]
            int? pvalueHistoryLength = null,
            [TransformFunctionParameter(Description = "Detect positive or negative anomalies, or both.")]
            AnomalySide side = AnomalySide.TwoSided
        )
        {
            var mlContext = new MLContext();

            var iidSpikeEstimator = mlContext.Transforms.DetectIidSpike(outputColumnName:
                nameof(AnomalyPrediction.Prediction), inputColumnName:
                nameof(AnomalyEntry.Value), confidence:
                confidence, pvalueHistoryLength:
                pvalueHistoryLength ?? _cacheSeries.Count / 4,
                side);

            var trainData = mlContext.Data.LoadFromEnumerable(SeriesValues());
            var trainedModel = iidSpikeEstimator.Fit(trainData);

            return Helpers.SaveModel(mlContext, trainData.Schema, trainedModel);
        }
        
        [TransformFunction(FunctionType = EFunctionType.Map, Category = "Machine Learning", Name = "Spike Predict", Description = "Predicts a spike from a model build using the spike detection model.", ResetMethod = nameof(Reset), ImportMethod = nameof(ImportModelLabels))]
        public AnomalyPrediction SpikePredict(
            [TransformFunctionParameter(Name = "Model", Description = "The spike detection model." )] byte[] model, 
            float value
        )
        {
            if (_predictionEngine == null)
            {
                var mlContext = new MLContext();
                var trainingModel = Helpers.LoadModel(mlContext, model, out _);
                _predictionEngine = mlContext.Model.CreatePredictionEngine<AnomalyEntry, AnomalyPrediction>(trainingModel);
            }

            var prediction = _predictionEngine.Predict(new AnomalyEntry() {Value = value});
            return prediction;
        }
    }
}