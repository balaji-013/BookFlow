using BookFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace BookFlow.API.Auth;

public static class Policies
{
    public const string Owner = "Owner";                 // Owner only (+SuperAdmin)
    public const string Manager = "Manager";             // Owner, StoreManager
    public const string Billing = "Billing";             // Owner, StoreManager, BillingEmployee
    public const string Inventory = "Inventory";         // Owner, StoreManager, InventoryManager
    public const string AnyStaff = "AnyStaff";           // every authenticated role

    public static void Configure(AuthorizationOptions o)
    {
        o.AddPolicy(Owner, p => p.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Owner)));
        o.AddPolicy(Manager, p => p.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Owner), nameof(UserRole.StoreManager)));
        o.AddPolicy(Billing, p => p.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Owner), nameof(UserRole.StoreManager), nameof(UserRole.BillingEmployee)));
        o.AddPolicy(Inventory, p => p.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Owner), nameof(UserRole.StoreManager), nameof(UserRole.InventoryManager)));
        o.AddPolicy(AnyStaff, p => p.RequireAuthenticatedUser());
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }
}
