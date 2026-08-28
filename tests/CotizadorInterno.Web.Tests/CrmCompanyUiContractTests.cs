using System.Reflection;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Crm;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmCompanyUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CreateCompanyIsAnAntiforgeryProtectedJsonPost()
    {
        var action = Assert.Single(
            typeof(CrmController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(CrmController.CreateCompany));

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());

        var request = Assert.Single(
            action.GetParameters(),
            parameter => parameter.ParameterType == typeof(CrmCompanyCreateRequest));
        Assert.NotNull(request.GetCustomAttribute<FromBodyAttribute>());
    }

    [Fact]
    public void CompanyDrawerCreatesLeadWithoutAnEditableLifecycle()
    {
        var view = ReadProjectFile("Views", "Crm", "Index.cshtml");

        Assert.Contains("data-create-company-url", view, StringComparison.Ordinal);
        Assert.Contains("data-company-drawer", view, StringComparison.Ordinal);
        Assert.Contains("data-company-form", view, StringComparison.Ordinal);
        Assert.Contains("data-open-company>Nueva empresa", view, StringComparison.Ordinal);
        Assert.Contains("La empresa se registrará como Lead.", view, StringComparison.Ordinal);

        var drawerStart = view.IndexOf("data-company-drawer", StringComparison.Ordinal);
        var contactDrawerStart = view.IndexOf("data-contact-drawer", drawerStart, StringComparison.Ordinal);
        var drawer = view[drawerStart..contactDrawerStart];
        Assert.DoesNotContain("name=\"Lifecycle\"", drawer, StringComparison.Ordinal);
    }

    [Fact]
    public void CompanyTypeControlsContactLifecycleAndCompanyFiltering()
    {
        var view = ReadProjectFile("Views", "Crm", "Index.cshtml");
        var script = ReadProjectFile("wwwroot", "js", "crm.js");

        Assert.Contains("data-company-lifecycle-filter", view, StringComparison.Ordinal);
        Assert.Contains("data-company-lifecycle=\"@company.LifecycleValue\"", view, StringComparison.Ordinal);
        Assert.Contains("data-contact-company", view, StringComparison.Ordinal);
        Assert.Contains("data-contact-lifecycle", view, StringComparison.Ordinal);
        Assert.Contains("function syncContactLifecycle()", script, StringComparison.Ordinal);
        Assert.Contains("contactLifecycleValues.lead", script, StringComparison.Ordinal);
        Assert.Contains("contactLifecycleValues.customer", script, StringComparison.Ordinal);
        Assert.Contains("contactLifecycleSelect.disabled = Boolean(targetLifecycle);", script, StringComparison.Ordinal);
        Assert.Contains("function applyCompanyFilter()", script, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("No se encontró la raíz del proyecto web.");
    }
}
