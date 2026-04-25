using OrderProcessing.Common;
using Xunit;

namespace OrderProcessing.Tests;

public class ConstantsTests
{
    [Fact]
    public void Order_lifecycle_statuses_are_defined()
    {
        Assert.Equal("Pending", OrderStatuses.Pending);
        Assert.Equal("Processing", OrderStatuses.Processing);
        Assert.Equal("Completed", OrderStatuses.Completed);
        Assert.Equal("Failed", OrderStatuses.Failed);
        Assert.Equal("Cancelled", OrderStatuses.Cancelled);
    }

    [Fact]
    public void Job_statuses_match_database_filtered_indexes()
    {
        Assert.Equal("Pending", JobStatuses.Pending);
        Assert.Equal("InProgress", JobStatuses.InProgress);
        Assert.Equal("Succeeded", JobStatuses.Succeeded);
        Assert.Equal("Failed", JobStatuses.Failed);
    }
}
