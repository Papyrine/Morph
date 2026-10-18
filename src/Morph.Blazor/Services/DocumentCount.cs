/// <summary>
/// What Word's Word Count dialog says of a document, or of what is selected in it. A word is a run of
/// characters between spaces, as Word counts one: "don't" and "e-mail" are one each.
/// </summary>
readonly record struct DocumentCount(int Words, int Characters, int CharactersWithSpaces, int Paragraphs = 0, int Lines = 0, int Pages = 0)
{
    public static DocumentCount Of(string text)
    {
        var words = 0;
        var characters = 0;
        var spaced = 0;
        var inWord = false;
        foreach (var character in text)
        {
            // A line break or a paragraph's end is no character of the text.
            if (character is '\n' or '\r')
            {
                inWord = false;
                continue;
            }

            spaced++;
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
                continue;
            }

            characters++;
            if (!inWord)
            {
                words++;
                inWord = true;
            }
        }

        return new(words, characters, spaced);
    }
}
