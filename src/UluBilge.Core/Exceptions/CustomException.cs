namespace UluBilge.Core.Exceptions
{
    public sealed class CustomException(string message = "İşlem gerçekleştirilemedi") : Exception(message)
    {
    }
}
