using System;
using System.Collections.Generic;
using System.Text;

namespace Maintenance_app.Reports.charts_work
{
    public class MaintenanceChartResult
    {
        public int Year { get; set; }
        public int Month { get; set; }

        public int Don { get; set; }
        public int Temp { get; set; }
        public int Undon { get; set; }
        public int Moveing { get; set; }

        public int MCount { get; set; }
        public decimal MPrice { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Branch_name { get; set; } = App.Branch_Name;
        public string User_name { get; set; } = App.login_user.User_Name;
        public decimal DonPrice { get; set; }
        public decimal TempPrice { get; set; }
        public decimal UndonPrice { get; set; }
        public decimal MoveingPrice { get; set; }


    }
}
