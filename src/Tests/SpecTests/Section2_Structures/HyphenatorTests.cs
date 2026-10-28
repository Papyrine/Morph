/// <summary>
/// The hyphenation points <see cref="Hyphenator"/> finds — TeX's American English patterns, at least two
/// letters before a break and three after. The words are the ones Word broke in hyphenation_auto and
/// hyphenation_suppressed, each of whose breaks has to be among the points.
/// </summary>
public class HyphenatorTests
{
    static string Marked(string word)
    {
        var points = Hyphenator.Points(word);
        var builder = new StringBuilder();
        for (var index = 0; index < word.Length; index++)
        {
            if (points.Contains(index))
            {
                builder.Append('-');
            }

            builder.Append(word[index]);
        }

        return builder.ToString();
    }

    [Test]
    [Arguments("telecommunication", "telecom-mu-ni-ca-tion")]
    [Arguments("Telecommunications", "Telecom-mu-ni-ca-tions")]
    [Arguments("hyphens", "hy-phens")]
    [Arguments("syllables", "syl-la-bles")]
    [Arguments("hyphenation", "hy-phen-ation")]
    public async Task Breaks_where_the_patterns_say(string word, string expected) =>
        await Assert.That(Marked(word)).IsEqualTo(expected);

    [Test]
    [Arguments("table", "ta-ble")]
    [Arguments("present", "present")]
    public async Task The_exception_list_wins(string word, string expected) =>
        await Assert.That(Marked(word)).IsEqualTo(expected);

    [Test]
    [Arguments("word")]
    [Arguments("x86-64")]
    [Arguments("e-mail")]
    public async Task Short_or_non_letter_words_do_not_break(string word) =>
        await Assert.That(Hyphenator.Points(word)).IsEmpty();
}
