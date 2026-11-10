static partial class FormulaFunctions
{
    static void RegisterDates(Action<FormulaFunction> add)
    {
        add(new("TODAY", 0, 0, _ => Number(Math.Floor(FormulaCoercion.ToSerial(_.Evaluator.Book.Now.Date))))
        {
            Volatile = true
        });
        add(new("NOW", 0, 0, _ => Number(FormulaCoercion.ToSerial(_.Evaluator.Book.Now)))
        {
            Volatile = true
        });
        add(new("DATE", 3, 3, Date));
        add(new("TIME", 3, 3, Time));
        add(new("YEAR", 1, 1, _ => DatePart(_, Year, 1900)));
        add(new("MONTH", 1, 1, _ => DatePart(_, Month, 1)));
        add(new("DAY", 1, 1, _ => DatePart(_, Day, 0)));
        add(new("HOUR", 1, 1, _ => TimePart(_, 3_600, 24)));
        add(new("MINUTE", 1, 1, _ => TimePart(_, 60, 60)));
        add(new("SECOND", 1, 1, _ => TimePart(_, 1, 60)));
        add(new("WEEKDAY", 1, 2, Weekday));
        add(new("WEEKNUM", 1, 2, WeekNumber));
        add(new("ISOWEEKNUM", 1, 1, IsoWeekNumber));
        add(new("EDATE", 2, 2, _ => MonthsOn(_, false)));
        add(new("EOMONTH", 2, 2, _ => MonthsOn(_, true)));
        add(new("DAYS", 2, 2, Days));
        add(new("DATEDIF", 3, 3, DateDifference));
        add(new("DATEVALUE", 1, 1, DateValue));
        add(new("TIMEVALUE", 1, 1, TimeValue));
        add(new("NETWORKDAYS", 2, 3, NetworkDays));
        add(new("WORKDAY", 2, 3, WorkDay));
    }

    // A serial argument as a date; before day 0 or past 9999 it is #NUM!.
    static FormulaValue Serial(FunctionArguments args, int index, out DateTime date)
    {
        date = default;
        var number = args.Number(index);
        if (number.IsError)
        {
            return number;
        }

        if (number.Number is < 0 or >= 2_958_466)
        {
            return Error(FormulaError.Number);
        }

        date = FormulaCoercion.FromSerial(number.Number);
        return number;
    }

    static FormulaValue Date(FunctionArguments args)
    {
        var year = Integer(args, 0, 0);
        if (year.IsError)
        {
            return year;
        }

        var month = Integer(args, 1, 0);
        if (month.IsError)
        {
            return month;
        }

        var day = Integer(args, 2, 0);
        if (day.IsError)
        {
            return day;
        }

        // A year under 1900 is counted from 1900, as Excel counts it: DATE(24, 1, 1) is 1924.
        var y = year.Number;
        if (y < 1900)
        {
            y += 1900;
        }

        if (y is < 1900 or > 9999)
        {
            return Error(FormulaError.Number);
        }

        try
        {
            var date = new DateTime((int) y, 1, 1)
                .AddMonths((int) month.Number - 1)
                .AddDays(day.Number - 1);
            var serial = FormulaCoercion.ToSerial(date);
            if (serial < 0)
            {
                return Error(FormulaError.Number);
            }

            return Number(serial);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error(FormulaError.Number);
        }
    }

    static FormulaValue Time(FunctionArguments args)
    {
        var hour = Integer(args, 0, 0);
        if (hour.IsError)
        {
            return hour;
        }

        var minute = Integer(args, 1, 0);
        if (minute.IsError)
        {
            return minute;
        }

        var second = Integer(args, 2, 0);
        if (second.IsError)
        {
            return second;
        }

        var total = hour.Number * 3_600 + minute.Number * 60 + second.Number;
        if (total < 0)
        {
            return Error(FormulaError.Number);
        }

        // Only the time of day: TIME(25, 0, 0) is 1:00.
        return Number(total % 86_400 / 86_400);
    }

    static FormulaValue DatePart(FunctionArguments args, Func<DateTime, int> part, int zero)
    {
        var serial = Serial(args, 0, out var date);
        if (serial.IsError)
        {
            return serial;
        }

        // Serial 0 is the 0th of January 1900, as Excel reads it.
        if (serial.Number < 1)
        {
            return Number(zero);
        }

        return Number(part(date));
    }

    static int Year(DateTime date) => date.Year;

    static int Month(DateTime date) => date.Month;

    static int Day(DateTime date) => date.Day;

    static FormulaValue TimePart(FunctionArguments args, int seconds, int modulus)
    {
        var number = args.Number(0);
        if (number.IsError)
        {
            return number;
        }

        if (number.Number < 0)
        {
            return Error(FormulaError.Number);
        }

        var ofDay = Math.Round((number.Number - Math.Floor(number.Number)) * 86_400);
        return Number(Math.Floor(ofDay / seconds) % modulus);
    }

    static FormulaValue Weekday(FunctionArguments args)
    {
        var serial = Serial(args, 0, out var date);
        if (serial.IsError)
        {
            return serial;
        }

        var kind = Integer(args, 1, 1);
        if (kind.IsError)
        {
            return kind;
        }

        // Sunday 0 to Saturday 6.
        var day = (int) date.DayOfWeek;
        switch ((int) kind.Number)
        {
            case 1 or 17:
                return Number(day + 1);
            case 2 or 11:
                return Number((day + 6) % 7 + 1);
            case 3:
                return Number((day + 6) % 7);
            case >= 12 and <= 16:
                // 12 starts the week on Tuesday, 16 on Saturday.
                var start = (int) kind.Number - 10;
                return Number((day - start + 7) % 7 + 1);
            default:
                return Error(FormulaError.Number);
        }
    }

    static FormulaValue WeekNumber(FunctionArguments args)
    {
        var serial = Serial(args, 0, out var date);
        if (serial.IsError)
        {
            return serial;
        }

        var kind = Integer(args, 1, 1);
        if (kind.IsError)
        {
            return kind;
        }

        int first;
        switch ((int) kind.Number)
        {
            case 1 or 17:
                first = 0;
                break;
            case 2 or 11:
                first = 1;
                break;
            case >= 12 and <= 16:
                first = (int) kind.Number - 10;
                break;
            case 21:
                return Number(ISOWeek.GetWeekOfYear(date));
            default:
                return Error(FormulaError.Number);
        }

        var january = new DateTime(date.Year, 1, 1);
        var offset = ((int) january.DayOfWeek - first + 7) % 7;
        return Number((date.DayOfYear - 1 + offset) / 7 + 1);
    }

    static FormulaValue IsoWeekNumber(FunctionArguments args)
    {
        var serial = Serial(args, 0, out var date);
        if (serial.IsError)
        {
            return serial;
        }

        return Number(ISOWeek.GetWeekOfYear(date));
    }

    static FormulaValue MonthsOn(FunctionArguments args, bool endOfMonth)
    {
        var serial = Serial(args, 0, out var date);
        if (serial.IsError)
        {
            return serial;
        }

        var months = Integer(args, 1, 0);
        if (months.IsError)
        {
            return months;
        }

        try
        {
            var moved = date.Date.AddMonths((int) months.Number);
            if (endOfMonth)
            {
                moved = new(moved.Year, moved.Month, DateTime.DaysInMonth(moved.Year, moved.Month));
            }

            return Number(FormulaCoercion.ToSerial(moved));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error(FormulaError.Number);
        }
    }

    static FormulaValue Days(FunctionArguments args)
    {
        var end = args.Number(0);
        if (end.IsError)
        {
            return end;
        }

        var start = args.Number(1);
        if (start.IsError)
        {
            return start;
        }

        return Number(Math.Floor(end.Number) - Math.Floor(start.Number));
    }

    static FormulaValue DateDifference(FunctionArguments args)
    {
        var first = Serial(args, 0, out var start);
        if (first.IsError)
        {
            return first;
        }

        var second = Serial(args, 1, out var end);
        if (second.IsError)
        {
            return second;
        }

        var unit = args.Text(2);
        if (unit.IsError)
        {
            return unit;
        }

        if (end < start)
        {
            return Error(FormulaError.Number);
        }

        start = start.Date;
        end = end.Date;
        var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
        if (end.Day < start.Day)
        {
            months--;
        }

        switch (unit.Text.ToUpperInvariant())
        {
            case "D":
                return Number((end - start).TotalDays);
            case "M":
                return Number(months);
            case "Y":
                return Number(months / 12);
            case "YM":
                return Number(months % 12);
            case "MD":
                var anchor = start.AddMonths(months);
                return Number((end - anchor).TotalDays);
            case "YD":
                var sameYear = start.AddYears(months / 12);
                return Number((end - sameYear).TotalDays);
            default:
                return Error(FormulaError.Number);
        }
    }

    static FormulaValue DateValue(FunctionArguments args)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        if (FormulaCoercion.ParseDate(text.Text) is { } serial and >= 1)
        {
            return Number(Math.Floor(serial));
        }

        return Error(FormulaError.Value);
    }

    static FormulaValue TimeValue(FunctionArguments args)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        if (FormulaCoercion.ParseDate(text.Text) is { } serial)
        {
            return Number(serial - Math.Floor(serial));
        }

        return Error(FormulaError.Value);
    }

    static HashSet<double> Holidays(FunctionArguments args, int index)
    {
        var holidays = new HashSet<double>();
        if (args.IsMissing(index))
        {
            return holidays;
        }

        foreach (var value in args.Array(index))
        {
            if (value.IsNumber)
            {
                holidays.Add(Math.Floor(value.Number));
            }
        }

        return holidays;
    }

    static bool Weekend(DateTime date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    static FormulaValue NetworkDays(FunctionArguments args)
    {
        var first = Serial(args, 0, out var start);
        if (first.IsError)
        {
            return first;
        }

        var last = Serial(args, 1, out var end);
        if (last.IsError)
        {
            return last;
        }

        var holidays = Holidays(args, 2);
        var sign = 1;
        if (end < start)
        {
            (start, end) = (end, start);
            sign = -1;
        }

        var count = 0;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            if (!Weekend(day) &&
                !holidays.Contains(FormulaCoercion.ToSerial(day)))
            {
                count++;
            }
        }

        return Number(sign * count);
    }

    static FormulaValue WorkDay(FunctionArguments args)
    {
        var first = Serial(args, 0, out var start);
        if (first.IsError)
        {
            return first;
        }

        var days = Integer(args, 1, 0);
        if (days.IsError)
        {
            return days;
        }

        var holidays = Holidays(args, 2);
        var step = Math.Sign(days.Number);
        var remaining = Math.Abs(days.Number);
        var day = start.Date;
        while (remaining > 0)
        {
            day = day.AddDays(step);
            if (!Weekend(day) &&
                !holidays.Contains(FormulaCoercion.ToSerial(day)))
            {
                remaining--;
            }
        }

        return Number(FormulaCoercion.ToSerial(day));
    }
}
