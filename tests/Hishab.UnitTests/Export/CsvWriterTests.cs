using Hishab.Infrastructure.Export;

namespace Hishab.UnitTests.Export;

public class CsvWriterTests
{
    private static async Task<string> Row(Action<CsvWriter> write)
    {
        using var text = new StringWriter();
        var csv = new CsvWriter(text);
        write(csv);
        await csv.EndRowAsync();
        return text.ToString();
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\",\"Click\")", "'=HYPERLINK(\"http://x\",\"Click\")")]
    [InlineData("+8801712345678", "'+8801712345678")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("Rahima Begum", "Rahima Begum")]
    [InlineData("রহিমা বেগম", "রহিমা বেগম")]
    [InlineData("a=b", "a=b")] // only the first character makes a formula
    public void Typed_text_that_starts_like_a_formula_is_neutralised(string input, string expected)
    {
        CsvWriter.Neutralise(input).ShouldBe(expected);
    }

    [Fact]
    public async Task Fields_with_commas_quotes_or_line_breaks_are_quoted_and_quotes_doubled()
    {
        var row = await Row(csv => csv.Text("House 4, Road 2").Text("He said \"call first\"").Text("line1\nline2").Text(null).Raw("plain"));

        row.ShouldBe("\"House 4, Road 2\",\"He said \"\"call first\"\"\",\"line1\nline2\",,plain\r\n");
    }

    [Fact]
    public async Task A_formula_with_a_comma_is_neutralised_and_quoted()
    {
        (await Row(csv => csv.Text("=1+2,3"))).ShouldBe("\"'=1+2,3\"\r\n");
    }

    [Fact]
    public async Task Numbers_we_write_stay_numbers_even_when_negative()
    {
        (await Row(csv => csv.Number(-180m).Number(1060.50m).Number((decimal?)null).Number(3))).ShouldBe("-180,1060.5,,3\r\n");
    }

    [Fact]
    public void The_encoding_writes_a_byte_order_mark_for_Excel()
    {
        CsvWriter.Encoding.GetPreamble().ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Theory]
    [InlineData("+8801912345678", "01912-345678")]
    [InlineData("+14155550100", "+14155550100")]
    public void Phones_are_written_the_Bangladeshi_way_and_stay_text_in_Excel(string e164, string expected)
    {
        ExportService.Phone(e164).ShouldBe(expected);
    }
}
