Baseline table (no indent, 100% width):

<table>
<tbody>
<tr>
<td>A</td>
<td>B</td>
</tr>
<tr>
<td>1</td>
<td>2</td>
</tr>
</tbody>
</table>

List-item indent (tblInd=480 dxa):

<table>
<tbody>
<tr>
<td>A</td>
<td>B</td>
</tr>
<tr>
<td>1</td>
<td>2</td>
</tr>
</tbody>
</table>

Block-quote indent (tblInd=720 dxa):

<table>
<tbody>
<tr>
<td>A</td>
<td>B</td>
</tr>
<tr>
<td>1</td>
<td>2</td>
</tr>
</tbody>
</table>

Doubly-nested quote (tblInd=1440 dxa):

<table>
<tbody>
<tr>
<td>A</td>
<td>B</td>
</tr>
<tr>
<td>1</td>
<td>2</td>
</tr>
</tbody>
</table>

Centred table with tblInd=720 (indent should collapse into slack):

<table style="width:32%;">
<colgroup>
<col style="width: 16%" />
<col style="width: 16%" />
</colgroup>
<tbody>
<tr>
<td>A</td>
<td>B</td>
</tr>
</tbody>
</table>
