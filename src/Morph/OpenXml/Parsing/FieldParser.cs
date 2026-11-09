using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlFieldCode = DocumentFormat.OpenXml.Wordprocessing.FieldCode;

/// <summary>
/// Reads complex fields: the field codes of the body and a legacy form field's
/// <c>w:ffData</c>.
/// </summary>
static class FieldParser
{
    public static IReadOnlyList<FieldCode> ExtractFieldCodes(BodyScan scan)
    {
        var result = new List<FieldCode>();
        var instructionStack = new Stack<StringBuilder>();
        var resultStack = new Stack<StringBuilder>();
        var inResult = new Stack<bool>();

        foreach (var run in scan.Runs)
        {
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Begin:
                        instructionStack.Push(new());
                        resultStack.Push(new());
                        inResult.Push(false);
                        break;

                    case OoxmlFieldCode instr when instructionStack.Count > 0:
                        if (!inResult.Peek())
                        {
                            instructionStack.Peek().Append(instr.Text);
                        }

                        break;

                    case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Separate &&
                                           inResult.Count > 0:
                        var flag = inResult.Pop();
                        inResult.Push(true);
                        _ = flag;
                        break;

                    case Text t when instructionStack.Count > 0 &&
                                     inResult.Peek():
                        resultStack.Peek().Append(t.Text.ReplaceSeparatorsWithSpace());
                        break;

                    case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.End &&
                                           instructionStack.Count > 0:
                        var instruction = instructionStack.Pop().TrimmedToString();
                        var resultText = resultStack.Pop().ToString();
                        inResult.Pop();
                        if (instruction.Length > 0)
                        {
                            result.Add(
                                new()
                                {
                                    Instruction = instruction,
                                    Result = resultText
                                });
                        }

                        break;
                }
            }
        }

        // Legacy w:fldSimple (single-element field). The instruction lives in @w:instr and the
        // cached result is the descendant text.
        foreach (var simple in scan.SimpleFields)
        {
            var rawInstruction = simple.Instruction?.Value;
            if (!rawInstruction.TryTrim(out var instruction))
            {
                continue;
            }

            var resultText = string.Concat(simple.Descendants<Text>().Select(_ => _.Text));
            result.Add(
                new()
                {
                    Instruction = instruction,
                    Result = resultText
                });
        }

        return result;
    }

    /// <summary>
    /// Parses a legacy form field from a run containing FormFieldData.
    /// </summary>
    public static FormFieldElement? ParseFormField(OoxmlRun run)
    {
        // Look for FieldChar with fldCharType="begin" carrying FormFieldData
        var fieldChar = run.GetFirstChild<FieldChar>();
        if (fieldChar?.FieldCharType?.Value != FieldCharValues.Begin)
        {
            return null;
        }

        // Word nests w:ffData INSIDE the w:fldChar element (it is the field character's
        // payload, not a run sibling); tolerate both placements.
        var ffData = fieldChar.GetFirstChild<FormFieldData>() ?? run.GetFirstChild<FormFieldData>();
        if (ffData == null)
        {
            return null;
        }

        // Get common properties
        var nameElement = ffData.GetFirstChild<FormFieldName>();
        var name = nameElement?.Val?.Value;

        var enabledElement = ffData.GetFirstChild<Enabled>();
        var enabled = enabledElement?.Val?.Value != false;

        // Check for checkbox
        var checkbox = ffData.GetFirstChild<CheckBox>();
        if (checkbox != null)
        {
            var checkedElement = checkbox.GetFirstChild<Checked>();
            // Default element may not have a strongly-typed class, search by local name
            var defaultElement = checkbox.ChildElements.FirstOrDefault(_ => _.LocalName == "default");
            var sizeElement = checkbox.GetFirstChild<FormFieldSize>();

            var isChecked = checkedElement != null &&
                            (checkedElement.Val == null || checkedElement.Val.Value);
            var defaultChecked = false;
            if (defaultElement != null)
            {
                // Check if it has a val attribute with false value
                var val = defaultElement.AttributeValue("val");
                defaultChecked = val == null ||
                                 (val != "0" &&
                                  !val.Equals("false", StringComparison.CurrentCultureIgnoreCase));
            }

            double size = 0;
            if (sizeElement?.Val?.HasValue == true &&
                double.TryParse(sizeElement.Val.Value, out var sizeValue))
            {
                size = sizeValue.HalfPointsToPoints();
            }

            return new CheckBoxFormFieldElement
            {
                Name = name,
                Enabled = enabled,
                Checked = isChecked,
                DefaultChecked = defaultChecked,
                SizePoints = size
            };
        }

        // Check for text input
        var textInput = ffData.GetFirstChild<TextInput>();
        if (textInput != null)
        {
            var typeElement = textInput.GetFirstChild<TextBoxFormFieldType>();
            var defaultElement = textInput.GetFirstChild<DefaultTextBoxFormFieldString>();
            var maxLengthElement = textInput.GetFirstChild<MaxLength>();

            var textType = TextFormFieldType.Regular;
            if (typeElement?.Val?.HasValue == true)
            {
                var val = typeElement.Val.Value;
                if (val == TextBoxFormFieldValues.Number)
                {
                    textType = TextFormFieldType.Number;
                }
                else if (val == TextBoxFormFieldValues.Date)
                {
                    textType = TextFormFieldType.Date;
                }
                else if (val == TextBoxFormFieldValues.CurrentDate)
                {
                    textType = TextFormFieldType.CurrentDate;
                }
                else if (val == TextBoxFormFieldValues.CurrentTime)
                {
                    textType = TextFormFieldType.CurrentTime;
                }
                else if (val == TextBoxFormFieldValues.Calculated)
                {
                    textType = TextFormFieldType.Calculated;
                }
            }

            return new TextFormFieldElement
            {
                Name = name,
                Enabled = enabled,
                DefaultText = defaultElement?.Val?.Value,
                Value = defaultElement?.Val?.Value ?? "",
                MaxLength = maxLengthElement?.Val?.Value ?? 0,
                TextType = textType,
                // Default width
                WidthPoints = 100
            };
        }

        // Check for drop-down list
        var dropDown = ffData.GetFirstChild<DropDownListFormField>();
        if (dropDown != null)
        {
            var items = dropDown.Elements<ListEntryFormField>()
                .Select(li => li.Val?.Value ?? "")
                .ToList();

            var resultElement = dropDown.GetFirstChild<DropDownListSelection>();
            var selectedIndex = resultElement?.Val?.Value ?? 0;

            return new DropDownFormFieldElement
            {
                Name = name,
                Enabled = enabled,
                Items = items,
                SelectedIndex = selectedIndex,
                // Default width
                WidthPoints = 100
            };
        }

        return null;
    }
}
