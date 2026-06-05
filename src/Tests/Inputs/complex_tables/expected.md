# **Complex Table Examples**
This document demonstrates various complex table features in Word documents.

## **1. Horizontally Merged Cells**
Tables can have cells that span multiple columns using columnSpan.

|**MERGED HEADER - SPANS 3 COLUMNS**|||
| :-: | :- | :- |
|**Column 1**|**Column 2**|**Column 3**|
|Data A1|Data B1|Data C1|
|**Merged: Columns 1 & 2**|Data C2||

## **2. Vertically Merged Cells**
Tables can have cells that span multiple rows using rowSpan.

<table><tr><th><b>Section</b></th><th><b>Item</b></th><th><b>Value</b></th></tr>
<tr><td rowspan="3"><b>Section A (Merged 3 rows)</b></td><td>Item 1</td><td>$100</td></tr>
<tr><td>Item 2</td><td>$200</td></tr>
<tr><td>Item 3</td><td>$300</td></tr>
<tr><td rowspan="2"><b>Section B (Merged 2 rows)</b></td><td>Item 4</td><td>$400</td></tr>
<tr><td>Item 5</td><td>$500</td></tr>
</table>

## **3. Nested Tables**
Tables can contain other tables within their cells for complex layouts.

|**NESTED TABLE EXAMPLE**||||
| :-: | :- | :- | :- |
|**Q1**|**Q2**|**Q3**|**Q4**|
|$25K||||

|**Apr**|**May**|
| :-: | :-: |
|$12K|$18K|

|||$35K|$40K|
| :-: | :- | :-: | :-: |

## **4. Complex Mixed Merging**
Combining both horizontal and vertical merges in the same table.

<table><tr><th colspan="2" rowspan="2"><b>COMPLEX MERGE</b></th><th><b>Col 3</b></th><th><b>Col 4</b></th><th><b>Col 5</b></th></tr>
<tr><td>Data 3-1</td><td colspan="2" valign="top"><b>Merged: Cols 4 & 5</b></td></tr>
<tr><td>A</td><td>B</td><td>C</td><td rowspan="1"><b>Vertical Merge</b></td><td>E</td></tr>
<tr><td colspan="3" valign="top"><b>Wide merge across 3 columns</b></td><td>F</td></tr>
</table>

## **5. Calendar-Style Layout**
A practical example showing how merging can create calendar-like structures.

|**January 2025**|||||||
| :-: | :- | :- | :- | :- | :- | :- |
|**Sun**|**Mon**|**Tue**|**Wed**|**Thu**|**Fri**|**Sat**|
|*Previous Month*|1|2|3|4|||
|5|6|7|8|9|10|11|
|12|13|14|**15-16 Event**|17|18||
