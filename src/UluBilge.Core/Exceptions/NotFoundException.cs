namespace UluBilge.Core.Exceptions
{
    public sealed class NotFoundException(string message="Kayıt bulunamadı") : Exception(message)
    {
    }
}
