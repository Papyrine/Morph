# Complex Table Examples

This document demonstrates various complex table features in Word documents.

## 1. Horizontally Merged Cells

Tables can have cells that span multiple columns using columnSpan.

| **MERGED HEADER - SPANS 3 COLUMNS** |  |  |
| --- | --- | --- |
| **Column 1** | **Column 2** | **Column 3** |
| Data A1 | Data B1 | Data C1 |
| **Merged: Columns 1 & 2** |  | Data C2 |

## 2. Vertically Merged Cells

Tables can have cells that span multiple rows using rowSpan.

| **Section** | **Item** | **Value** |
| --- | --- | --- |
| **Section A<br>(Merged 3 rows)** | Item 1 | $100 |
|  | Item 2 | $200 |
|  | Item 3 | $300 |
| **Section B<br>(Merged 2 rows)** | Item 4 | $400 |
|  | Item 5 | $500 |

## 3. Nested Tables

Tables can contain other tables within their cells for complex layouts.

| **NESTED TABLE EXAMPLE** |  |  |  |
| --- | --- | --- | --- |
| **Q1** | **Q2** | **Q3** | **Q4** |
| $25K | **Apr**<br>**May**<br>$12K<br>$18K | $35K | $40K |

## 4. Complex Mixed Merging

Combining both horizontal and vertical merges in the same table.

| **COMPLEX<br>MERGE** |  | **Col 3** | **Col 4** | **Col 5** |
| --- | --- | --- | --- | --- |
|  |  | Data 3-1 | **Merged: Cols 4 & 5** |  |
| A | B | C | **Vertical<br>Merge** | E |
| **Wide merge across 3 columns** |  |  |  | F |

## 5. Calendar-Style Layout

A practical example showing how merging can create calendar-like structures.

| **January 2025** |  |  |  |  |  |  |
| --- | --- | --- | --- | --- | --- | --- |
| **Sun** | **Mon** | **Tue** | **Wed** | **Thu** | **Fri** | **Sat** |
| *Previous Month* |  |  | 1 | 2 | 3 | 4 |
| 5 | 6 | 7 | 8 | 9 | 10 | 11 |
| 12 | 13 | 14 | **15-16<br>Event** |  | 17 | 18 |
