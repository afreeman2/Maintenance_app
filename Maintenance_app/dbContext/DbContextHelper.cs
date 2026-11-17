using Maintenance_app.dbContext;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Maintenance_app.DatabContext
{
    class DbContextHelper
    {
        public static DbContextOptions<Maintenance_DabaseContext>? Options { get; private set; }

        public static void InitOptions(string connectionString)
        {
            var builder = new DbContextOptionsBuilder<Maintenance_DabaseContext>();
            builder.UseSqlServer(connectionString);

            Options = builder.Options;
        }
    }
}
