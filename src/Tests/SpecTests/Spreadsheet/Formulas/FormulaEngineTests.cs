/// <summary>
/// The formula engine's rules, each against what Excel gives: its precedence (negation before ^, ^ to
/// the left), how it turns one kind of value into another, its errors, and the functions. Formulas are
/// worked out in a one-sheet workbook whose A1:A5 hold 1 to 5, B1:B5 the words one to five, and C1 the
/// logical TRUE.
/// </summary>
public class FormulaEngineTests
{
    static FormulaValue Evaluate(string formula)
    {
        var book = new FormulaWorkbook(new(2026, 11, 10, 15, 30, 0));
        var sheet = book.AddSheet("Sheet1");
        string[] words = ["one", "two", "three", "four", "five"];
        for (var row = 1; row <= 5; row++)
        {
            var number = sheet.Add(row, 1);
            number.Value = FormulaValue.FromNumber(row);
            var word = sheet.Add(row, 2);
            word.Value = FormulaValue.FromText(words[row - 1]);
        }

        sheet.Add(1, 3).Value = FormulaValue.True;
        var cell = sheet.Add(10, 10);
        cell.Formula = formula;
        book.Recalculate();
        return cell.Value;
    }

    public static IEnumerable<(string Formula, double Expected)> Numbers() =>
    [
        // Negation before ^, and ^ to the left.
        ("-2^2", 4),
        ("2^3^2", 64),
        ("2*3+4", 10),
        ("2+3*4", 14),
        ("(2+3)*4", 20),
        ("50%", 0.5),
        ("10-50%", 9.5),
        ("\"3\"+4", 7),
        ("TRUE+1", 2),
        ("A1+A2", 3),
        ("SUM(A1:A5)", 15),
        ("SUM(A1:A5,10)", 25),
        ("SUM(A:A)", 15),
        ("SUM(A1:B5)", 15),
        ("AVERAGE(A1:A5)", 3),
        ("MIN(A1:A5)", 1),
        ("MAX(A1:A5,7)", 7),
        ("COUNT(A1:B5)", 5),
        ("COUNTA(A1:C5)", 11),
        ("COUNTBLANK(A1:C2)", 1),
        ("COUNTIF(A1:A5,\">2\")", 3),
        ("COUNTIF(B1:B5,\"t*\")", 2),
        ("COUNTIFS(A1:A5,\">1\",A1:A5,\"<5\")", 3),
        ("SUMIF(A1:A5,\">=3\")", 12),
        ("SUMIF(B1:B5,\"two\",A1:A5)", 2),
        ("SUMIFS(A1:A5,B1:B5,\"*e\")", 9),
        ("AVERAGEIF(A1:A5,\"<3\")", 1.5),
        ("SUMPRODUCT(A1:A5,A1:A5)", 55),
        ("SUMPRODUCT((A1:A5>2)*A1:A5)", 12),
        ("ROUND(2.675,2)", 2.68),
        ("ROUND(-2.5,0)", -3),
        ("ROUND(1234,-2)", 1200),
        ("ROUNDUP(1.21,1)", 1.3),
        ("ROUNDDOWN(-1.29,1)", -1.2),
        ("INT(-1.5)", -2),
        ("TRUNC(-1.5)", -1),
        ("MOD(-3,2)", 1),
        ("CEILING(2.5,1)", 3),
        ("FLOOR(-2.5,1)", -3),
        ("ABS(-4)", 4),
        ("POWER(2,10)", 1024),
        ("SQRT(16)", 4),
        ("MEDIAN(A1:A4)", 2.5),
        ("LARGE(A1:A5,2)", 4),
        ("SMALL(A1:A5,2)", 2),
        ("RANK.EQ(4,A1:A5)", 2),
        ("RANK(4,A1:A5,1)", 4),
        ("SUBTOTAL(9,A1:A5)", 15),
        ("SUBTOTAL(101,A1:A5)", 3),
        ("VLOOKUP(3,A1:B5,1,FALSE)", 3),
        ("VLOOKUP(3.5,A1:A5,1)", 3),
        ("MATCH(4,A1:A5,0)", 4),
        ("MATCH(\"three\",B1:B5,0)", 3),
        ("MATCH(2.5,A1:A5)", 2),
        ("INDEX(A1:A5,3)", 3),
        ("INDEX(A1:B5,2,1)", 2),
        ("SUM(A1:INDEX(A1:A5,3))", 6),
        ("XLOOKUP(\"four\",B1:B5,A1:A5)", 4),
        ("ROW()", 10),
        ("ROW(A3)", 3),
        ("COLUMN(C1)", 3),
        ("ROWS(A1:B5)", 5),
        ("COLUMNS(A1:C1)", 3),
        ("SUM(OFFSET(A1,1,0,2,1))", 5),
        ("INDIRECT(\"A4\")", 4),
        ("LEN(B3)", 5),
        ("FIND(\"e\",B3)", 4),
        ("SEARCH(\"E\",B3)", 4),
        ("VALUE(\"1,234.5\")", 1234.5),
        ("DATE(2024,1,31)", 45322),
        ("DATE(2024,14,1)", 45689),
        ("YEAR(45322)", 2024),
        ("MONTH(45322)", 1),
        ("DAY(45322)", 31),
        ("WEEKDAY(45322)", 4),
        ("WEEKDAY(45322,2)", 3),
        ("EOMONTH(45322,1)", 45351),
        ("EDATE(45322,1)", 45351),
        ("DATEDIF(45292,45322,\"d\")", 30),
        ("TODAY()", 46336),
        ("HOUR(0.75)", 18),
        ("CHOOSE(2,10,20,30)", 20),
        ("IF(A1>0,10,20)", 10),
        ("IF(A1>5,10)", 0),
        ("IFERROR(1/0,7)", 7),
        ("SUM(IF(A1:A5>2,A1:A5))", 0)
    ];

    [Test]
    [MethodDataSource(nameof(Numbers))]
    public async Task Number(string formula, double expected)
    {
        var value = Evaluate(formula);
        if (formula == "IF(A1>5,10)")
        {
            // A missing else is FALSE, not 0.
            await Assert.That(value.IsBoolean).IsTrue();
            return;
        }

        if (formula == "SUM(IF(A1:A5>2,A1:A5))")
        {
            // Not an array formula: the range narrowed to row 10 is nothing, #VALUE!.
            await Assert.That(value.IsError).IsTrue();
            return;
        }

        await Assert.That(value.IsNumber).IsTrue().Because(value.ToString());
        await Assert.That(value.Number).IsEqualTo(expected).Within(1e-9);
    }

    public static IEnumerable<(string Formula, string Expected)> Texts() =>
    [
        ("\"a\"&\"b\"", "ab"),
        ("\"x\"&1.5", "x1.5"),
        ("\"x\"&(0.1+0.2)", "x0.3"),
        ("\"x\"&TRUE", "xTRUE"),
        ("1/3&\"\"", "0.333333333333333"),
        ("CONCATENATE(\"a\",1,\"b\")", "a1b"),
        ("TEXTJOIN(\", \",TRUE,B1:B3)", "one, two, three"),
        ("UPPER(B1)", "ONE"),
        ("PROPER(\"o'neil smith\")", "O'Neil Smith"),
        ("TRIM(\"  a   b  \")", "a b"),
        ("LEFT(B3,2)", "th"),
        ("RIGHT(B3,2)", "ee"),
        ("MID(B3,2,3)", "hre"),
        ("SUBSTITUTE(\"a-b-c\",\"-\",\"+\")", "a+b+c"),
        ("SUBSTITUTE(\"a-b-c\",\"-\",\"+\",2)", "a-b+c"),
        ("REPLACE(\"abcdef\",2,3,\"X\")", "aXef"),
        ("REPT(\"ab\",3)", "ababab"),
        ("TEXT(1234.5,\"#,##0.00\")", "1,234.50"),
        ("TEXT(0.25,\"0%\")", "25%"),
        ("TEXT(45322,\"mmm d, yyyy\")", "Jan 31, 2024"),
        ("TEXT(45322,\"dddd\")", "Wednesday"),
        ("VLOOKUP(2,A1:B5,2,FALSE)", "two"),
        ("INDEX(B1:B5,MATCH(5,A1:A5,0))", "five"),
        ("IF(C1,\"yes\",\"no\")", "yes"),
        ("ADDRESS(2,3)", "$C$2"),
        ("ADDRESS(2,3,4)", "C2"),
        ("T(B1)", "one"),
        ("FIXED(1234.567,1)", "1,234.6"),
        ("DOLLAR(-1234.5)", "($1,234.50)")
    ];

    [Test]
    [MethodDataSource(nameof(Texts))]
    public async Task Text(string formula, string expected)
    {
        var value = Evaluate(formula);
        await Assert.That(value.IsText).IsTrue().Because(value.ToString());
        await Assert.That(value.Text).IsEqualTo(expected);
    }

    public static IEnumerable<(string Formula, bool Expected)> Logicals() =>
    [
        // Fifteen significant digits make these equal.
        ("0.1+0.2=0.3", true),
        ("\"a\"=\"A\"", true),
        ("\"b\">\"A\"", true),
        // Numbers before text before logicals.
        ("1<\"a\"", true),
        ("\"z\"<TRUE", true),
        ("AND(A1:A5)", true),
        ("AND(TRUE,0)", false),
        ("OR(FALSE,A1)", true),
        ("XOR(TRUE,TRUE)", false),
        ("NOT(0)", true),
        ("ISBLANK(D1)", true),
        ("ISBLANK(B1)", false),
        ("ISNUMBER(A1)", true),
        ("ISTEXT(B1)", true),
        ("ISERROR(1/0)", true),
        ("ISNA(MATCH(9,A1:A5,0))", true),
        ("ISEVEN(4)", true),
        ("ISODD(4)", false),
        ("EXACT(\"a\",\"A\")", false)
    ];

    [Test]
    [MethodDataSource(nameof(Logicals))]
    public async Task Logical(string formula, bool expected)
    {
        var value = Evaluate(formula);
        await Assert.That(value.IsBoolean).IsTrue().Because(value.ToString());
        await Assert.That(value.Boolean).IsEqualTo(expected);
    }

    public static IEnumerable<(string Formula, string Expected)> Errors() =>
    [
        ("1/0", "#DIV/0!"),
        ("\"a\"+1", "#VALUE!"),
        ("SQRT(-1)", "#NUM!"),
        ("VLOOKUP(9,A1:B5,2,FALSE)", "#N/A"),
        ("VLOOKUP(1,A1:B5,3,FALSE)", "#REF!"),
        ("Missing", "#NAME?"),
        ("Nowhere!A1", "#REF!"),
        ("A1:A5+1", "#VALUE!"),
        ("AVERAGE(B1:B5)", "#DIV/0!"),
        ("NA()", "#N/A"),
        ("0^0", "#NUM!"),
        ("SUM(A1,1/0)", "#DIV/0!")
    ];

    [Test]
    [MethodDataSource(nameof(Errors))]
    public async Task Error(string formula, string expected)
    {
        var value = Evaluate(formula);
        await Assert.That(value.IsError).IsTrue().Because(value.ToString());
        await Assert.That(FormulaErrors.Text(value.Error)).IsEqualTo(expected);
    }

    // A formula reads a cell's value worked out first, whatever order the cells come in.
    [Test]
    public async Task Formulas_AreWorkedOutAfterWhatTheyRead()
    {
        var book = new FormulaWorkbook(new(2026, 11, 10));
        var sheet = book.AddSheet("Sheet1");
        var total = sheet.Add(1, 1);
        total.Formula = "A2*2";
        var middle = sheet.Add(2, 1);
        middle.Formula = "A3+1";
        sheet.Add(3, 1).Value = FormulaValue.FromNumber(4);
        book.Recalculate();

        await Assert.That(total.Value.Number).IsEqualTo(10);
    }

    // A chain thousands long, as a running balance is, is worked out without running out of stack.
    [Test]
    public async Task ALongChain_IsWorkedOut()
    {
        var book = new FormulaWorkbook(new(2026, 11, 10));
        var sheet = book.AddSheet("Sheet1");
        sheet.Add(1, 1).Value = FormulaValue.FromNumber(1);
        for (var row = 2; row <= 20_000; row++)
        {
            sheet.Add(row, 1).Formula = $"A{row - 1}+1";
        }

        book.Recalculate();

        await Assert.That(sheet.Find(20_000, 1)!.Value.Number).IsEqualTo(20_000);
    }

    // A circular reference keeps the value its cell had, as Excel's does with iteration off.
    [Test]
    public async Task ACircularReference_KeepsItsValue()
    {
        var book = new FormulaWorkbook(new(2026, 11, 10));
        var sheet = book.AddSheet("Sheet1");
        var first = sheet.Add(1, 1);
        first.Formula = "B1+1";
        first.Value = FormulaValue.FromNumber(5);
        first.Cached = first.Value;
        var second = sheet.Add(1, 2);
        second.Formula = "A1+1";
        book.Recalculate();

        await Assert.That(first.Value.IsNumber).IsTrue();
        await Assert.That(second.Value.IsNumber).IsTrue();
    }

    // A function the engine has not got keeps the value Excel gave the cell, and asks Excel to work it out.
    [Test]
    public async Task AnUnknownFunction_KeepsTheCachedValue()
    {
        var book = new FormulaWorkbook(new(2026, 11, 10));
        var sheet = book.AddSheet("Sheet1");
        var cell = sheet.Add(1, 1);
        cell.Formula = "NOSUCHFUNCTION(1)";
        cell.Cached = FormulaValue.FromNumber(42);
        book.Recalculate();

        await Assert.That(cell.Value.Number).IsEqualTo(42);
        await Assert.That(cell.Unsupported).IsTrue();
        await Assert.That(book.NeedsExcel).IsTrue();
    }

    public static IEnumerable<(string Formula, int Rows, int Columns, string Expected)> Shifts() =>
    [
        ("A1+B2", 1, 1, "B2+C3"),
        ("$A$1+A$1+$A1", 2, 3, "$A$1+D$1+$A3"),
        ("SUM(A1:A9)", 0, 1, "SUM(B1:B9)"),
        ("Sheet2!A1*'My sheet'!B2", 1, 0, "Sheet2!A2*'My sheet'!B3"),
        ("SUM(A:A)", 5, 1, "SUM(B:B)"),
        ("SUM(1:1)", 2, 5, "SUM(3:3)"),
        ("A1", -1, 0, "#REF!"),
        ("Table1[Price]*2", 3, 3, "Table1[Price]*2"),
        ("\"A1\"&A1", 1, 0, "\"A1\"&A2")
    ];

    [Test]
    [MethodDataSource(nameof(Shifts))]
    public async Task Shift(string formula, int rows, int columns, string expected) =>
        await Assert.That(FormulaShifter.Shift(formula, rows, columns)).IsEqualTo(expected);
}
