using Maintenance_app.dbContext;
using Maintenance_app.Models;
using DevExpress.Data.Filtering;
using DevExpress.Data.Linq;
using DevExpress.Mvvm;
 using Microsoft.EntityFrameworkCore;

namespace Maintenance_app.viewo_Model
{
    public class Users_view_model : ViewModelBase, IDisposable
    {
        private readonly DbContextOptions<Maintenance_DabaseContext>? _options;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private EntityInstantFeedbackSource? _itemsSource;
        private bool _disposed;
        private int _refreshVersion;

        public Users_view_model()
        {
            if (IsInDesignMode)
                return;

            // Keep this window's database settings independent of other windows.
            _options = new DbContextOptionsBuilder<Maintenance_DabaseContext>()
                .UseSqlServer(
                    App.ConnectionString)
                .Options;
        }

        public EntityInstantFeedbackSource? ItemsSource
        {
            get
            {
                if (IsInDesignMode || _disposed)
                    return null;

                if (_itemsSource == null)
                {
                    _itemsSource = new EntityInstantFeedbackSource
                    {
                        KeyExpression = nameof(T_SystemUser.User_ID),
                        DefaultSorting = nameof(T_SystemUser.User_ID) + " ASC"
                    };
                    _itemsSource.GetQueryable += OnGetQueryable;
                    _itemsSource.DismissQueryable += OnDismissQueryable;
                }

                return _itemsSource;
            }
        }

        public int UsersCount
        {
            get => GetProperty(() => UsersCount);
            private set => SetProperty(() => UsersCount, value);
        }

        public string SearchText
        {
            get => GetProperty(() => SearchText) ?? string.Empty;
            set
            {
                if (SetProperty(() => SearchText, value ?? string.Empty))
                    RaisePropertyChanged(nameof(FilterCriteria));
            }
        }

        public CriteriaOperator? FilterCriteria => string.IsNullOrWhiteSpace(SearchText)
            ? null
            : new FunctionOperator(
                FunctionOperatorType.Contains,
                new OperandProperty(nameof(T_SystemUser.User_Name)),
                new OperandValue(SearchText.Trim()));

        internal Maintenance_DabaseContext CreateContext()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Users_view_model));

            return new Maintenance_DabaseContext(_options
                ?? throw new InvalidOperationException("لا يتوفر اتصال بقاعدة البيانات في وضع التصميم."));
        }

        public async Task RefreshAsync()
        {
            if (IsInDesignMode || _disposed)
                return;

            int version = ++_refreshVersion;
            ItemsSource?.Refresh();
            await using var context = CreateContext();
            int count = await context.T_SystemUsers
                .CountAsync(user => user.st == true, _lifetimeCancellation.Token);

            // Ignore a count from an earlier refresh or a window that has closed.
            if (!_disposed && version == _refreshVersion)
                UsersCount = count;
        }

        public async Task<T_SystemUser?> GetUserAsync(int userId)
        {
            await using var context = CreateContext();
            return await context.T_SystemUsers.AsNoTracking()
                .FirstOrDefaultAsync(
                    user => user.User_ID == userId && user.st == true,
                    _lifetimeCancellation.Token);
        }

        private void OnGetQueryable(object? sender, GetQueryableEventArgs e)
        {
            // DevExpress owns each background query until DismissQueryable.
            var context = new Maintenance_DabaseContext(_options!);
            e.AreSourceRowsThreadSafe = false;
            e.QueryableSource = UserActivityService.Query(context).AsNoTracking();
            e.Tag = context;
        }

        private static void OnDismissQueryable(object? sender, GetQueryableEventArgs e)
        {
            (e.Tag as Maintenance_DabaseContext)?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifetimeCancellation.Cancel();
            _itemsSource?.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

}
