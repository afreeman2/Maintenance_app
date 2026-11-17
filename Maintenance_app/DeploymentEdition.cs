namespace Maintenance_app;

internal static class DeploymentEdition
{
    #region ميزات نسخة التوزيع

    // يحدد بناء المثبّت النسخة نفسها في التطبيق والمثبّت، بصرف النظر عن صلاحيات الحساب.
#if MAINTENANCE_BRANCH_EDITION
    internal static bool IncludesBranchReports => false;
#else
    internal static bool IncludesBranchReports => true;
#endif

    #endregion
}
