/// <summary>One find result: a zero-based page and a character range of that page's text layer text.</summary>
readonly record struct TextMatch(int Page, int Start, int Length);
