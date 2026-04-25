namespace OrderProcessing.Common;

/// <summary>Stored in <c>Permissions.PermissionCode</c> and emitted on JWT as <see cref="AppClaims.Permission"/>.</summary>
public static class PermissionCodes
{
    public const string ProductsManage = "products.manage";
    public const string OrdersCreate = "orders.create";
    public const string OrdersReadOwn = "orders.read_own";
    public const string OrdersReadAll = "orders.read_all";
    public const string AddressesManage = "addresses.manage";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ProductsManage,
        OrdersCreate,
        OrdersReadOwn,
        OrdersReadAll,
        AddressesManage
    };

    /// <summary>Admin: catalog + all orders. Address book is customer-only.</summary>
    public static readonly IReadOnlyList<string> Admin = new[]
    {
        ProductsManage,
        OrdersReadAll,
        OrdersCreate
    };

    public static readonly IReadOnlyList<string> Customer = new[]
    {
        OrdersCreate,
        OrdersReadOwn,
        AddressesManage
    };
}

/// <summary>Must match policies registered in <c>Program.cs</c> (<c>Perm:{code}</c>).</summary>
public static class PolicyNames
{
    public const string ProductsManage = "Perm:products.manage";
    public const string OrdersCreate = "Perm:orders.create";
    public const string AddressesManage = "Perm:addresses.manage";
    public const string OrdersList = "Orders.List";
    public const string OrdersRead = "Orders.Read";
}
