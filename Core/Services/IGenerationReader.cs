namespace TiaOpennessApp.Services
{
    public interface IGenerationReader
    {
        Models.Generation.GenerationParseResult Read();
    }
}