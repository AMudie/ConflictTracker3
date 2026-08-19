
namespace ConflictCalc.Enumerations
{
    public enum PeriodFrequency
    {        Monthly = 28,
        Quarterly = 84
    }

    public enum MLAlgorithmBinaryClassification
    {
        SDCALogisticRegression, // fast, reliable, great baseline.
        FastTreeBinaryClassification, //Gradient‑boosted decision trees. Often one of the best performers on tabular data.
        LightGBMBinaryClassification, //Microsoft’s gradient boosting library. Excellent accuracy, handles categorical + numerical features wel
        AveragedPerceptron //Simple linear classifier. Good for very large datasets.
    }

    public enum MLAlgorithmRegression
    {
        SDCARegression, //Fast linear regression. Good baseline.
        BoostedTrees, //Handles non‑linear relationships well.
        LightGBMRegression,//Usually the strongest performer for tabular regression problems.
        FastForestRegression//Random forest regression
    }

    public enum MLAlgorithmTimeSeries
    {
        SSA, //Singular Spectrum Analysis. Good for univariate time series forecasting.
        FastTreeTweedie, //Gradient‑boosted decision trees for regression. Can handle non‑linear relationships.
        LightGBMRegression //Microsoft’s gradient boosting library. Excellent accuracy, handles categorical + numerical features well.
    }

    public enum ConflictDefinition
    {
        LocalTotalFatalities, // Local average fatalities
        RegionalTotalFatalities,
        LocalAvgSeverity, // Local average severity
        RegionalAvgSeverity
    }

    public enum TimePrediction
    {
        OneMonth,
        ThreeMonths,
        SixMonths, 
        TwelveMonths
    }
}