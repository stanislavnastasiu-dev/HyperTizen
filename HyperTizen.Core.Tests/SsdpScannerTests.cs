using HyperTizen.Core;

namespace HyperTizen.Core.Tests;

public class SsdpScannerTests
{
    [Theory]
    [InlineData("http://192.168.1.5:8090/description.xml", "http://192.168.1.5:8090")]
    [InlineData("https://192.168.1.5:8092/description.xml", "https://192.168.1.5:8092")]
    [InlineData("http://192.168.1.5:8091/some/where/description.xml?x=1", "http://192.168.1.5:8091")]
    public void The_server_is_where_its_description_is(string location, string expected)
    {
        Assert.Equal(expected, SsdpScanner.ServerUrl(new Uri(location)));
    }

    // HyperHDR 21 answers a search with "http://<address>:0/description.xml".
    [Fact]
    public void A_description_at_port_zero_means_the_usual_port()
    {
        Assert.Equal("http://192.168.1.5:8090", SsdpScanner.ServerUrl(new Uri("http://192.168.1.5:0/description.xml")));
    }

    [Fact]
    public void Something_that_is_not_a_web_address_is_no_server()
    {
        Assert.Null(SsdpScanner.ServerUrl(null));
        Assert.Null(SsdpScanner.ServerUrl(new Uri("description.xml", UriKind.Relative)));
        Assert.Null(SsdpScanner.ServerUrl(new Uri("ftp://192.168.1.5/description.xml")));
    }

    [Theory]
    [InlineData("urn:hyperhdr.eu:device:basic:1", "HyperHDR")]
    [InlineData("urn:hyperion-project.org:device:basic:1", "Hyperion")]
    public void A_server_without_a_readable_description_is_named_after_its_kind(string type, string expected)
    {
        Assert.Equal(expected, SsdpScanner.DefaultName(type));
    }
}
