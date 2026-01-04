public class Unzipper
{
    [Test]
    [Explicit]
    public async Task Run()
    {
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        foreach (var docx in Directory.GetFiles(inputsDir, "input.docx", SearchOption.AllDirectories))
        {
            var inputDirectory = Path.Combine(Path.GetDirectoryName(docx)!,"input");
            Console.WriteLine(inputDirectory);
            Directory.Delete(inputDirectory, true);
            Directory.CreateDirectory(inputDirectory);
            await using var stream = File.OpenRead(docx);
            await using var archive = new ZipArchive(stream);
            await archive.ExtractToDirectoryAsync(inputDirectory);
        }
    }
}