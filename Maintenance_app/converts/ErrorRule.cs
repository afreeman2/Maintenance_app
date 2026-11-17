using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Controls;

namespace Maintenance_app.converts
{
    public class ErrorRule : ValidationRule
    {
        public static string ErrorMessage { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {

            if (value != null)
            {
                int Er_Id = (int)value;
                if (Er_Id == 1)
                    return new ValidationResult(false, ErrorMessage);
            }


            return new ValidationResult(true, null);



        }





    }

}
