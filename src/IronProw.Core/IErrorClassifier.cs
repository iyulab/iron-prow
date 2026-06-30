namespace IronProw.Core;

/// <summary>Classifies inference exceptions into a recovery strategy.</summary>
public interface IErrorClassifier
{
    /// <summary>Classifies the exception.</summary>
    ErrorClassification Classify(Exception ex);
}
