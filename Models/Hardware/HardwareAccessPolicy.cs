using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Hardware;

public static class HardwareAccessPolicy
{
    public const string SupplierPaymentEmail = "cartera@digitaltechcolombia.com";
    public const string BillingEmail = "msuarez@digitaltechcolombia.com";
    public const string ImpersonationEmail = "sruiz@digitaltechcolombia.com";
    public const int WaitingDocumentationStateValue = 645250000;
    public const int OkForSupplierPaymentStateValue = 645250001;
    public const string OkForSupplierPaymentStateLabel = "Ok para pago a proveedor";
    public const int PaidToSupplierStateValue = 645250002;
    public const int DeliveredAwaitingBillingStateValue = 645250004;
    public const string SupplierPaymentFileField = "cr07a_pagoaproveedor";
    public const string SupplierPaymentActionKey = "register-supplier-payment";

    public static bool IsSupplierPaymentUser(CurrentUserInfo? user) =>
        HasEmail(user, SupplierPaymentEmail);

    public static bool IsBillingUser(CurrentUserInfo? user) =>
        HasEmail(user, BillingEmail);

    public static bool IsImpersonationUser(CurrentUserInfo? user) =>
        HasEmail(user, ImpersonationEmail);

    public static bool HasEmail(CurrentUserInfo? user, string expectedEmail)
    {
        if (user is null || string.IsNullOrWhiteSpace(expectedEmail))
            return false;

        return EmailMatches(user.Email, expectedEmail)
            || EmailMatches(user.EmployeeUserEmail, expectedEmail);
    }

    public static bool EmailMatches(string? actualEmail, string expectedEmail) =>
        string.Equals(NormalizeEmail(actualEmail), NormalizeEmail(expectedEmail), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEmail(string? email) =>
        (email ?? "").Trim();
}
