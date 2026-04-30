using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Hardware;

public static class HardwareAccessPolicy
{
    public const string SupplierPaymentEmail = "cartera@digitaltechcolombia.com";
    public const string ImpersonationEmail = "sruiz@digitaltechcolombia.com";
    public const int OkForSupplierPaymentStateValue = 645250001;
    public const string ProformaFileField = "cr07a_adjuntarproforma";
    public const string SupplierPaymentFileField = "cr07a_pagoaproveedor";
    public const string SupplierPaymentActionKey = "register-supplier-payment";

    public static bool IsSupplierPaymentUser(CurrentUserInfo? user) =>
        HasEmail(user, SupplierPaymentEmail);

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
