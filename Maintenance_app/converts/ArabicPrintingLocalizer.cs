using DevExpress.Xpf.Printing;
using System;
using System.Collections.Generic;
using System.Text;

namespace Maintenance_app.converts
{
    public class ArabicPrintingLocalizer : PrintingLocalizer
    {
        public override string GetLocalizedString(PrintingStringId id)
        {
            switch (id)
            {
                case PrintingStringId.Print:
                    return "طباعة";
                case PrintingStringId.PrintOneSided:
                    return "طباعة بجانب واحد";
                case PrintingStringId.PrintOneSidedDescription:
                    return "وصف الطباعة من جانب واحد";
                case PrintingStringId.PrintOnBothSides:
                    return "طباعة علي الوجهين";
                case PrintingStringId.PrintCopies:
                    return "عدد النسخ";
                case PrintingStringId.PrintPreviewTitle:
                    return "طباعة";

                case PrintingStringId.PrintPreviewSettings:
                    return "إعداد طباعة التقرير";
                case PrintingStringId.Zoom_Hint:
                    return "قم بتغيير مستوى التكبير/التصغير الحالي لمعاينة المستند";
                case PrintingStringId.DecreaseZoom_Hint:
                    return "قم بتصغير العرض لرؤية المزيد من الصفحة بحجم أصغر";
                case PrintingStringId.IncreaseZoom_Hint:
                    return "قم بالتكبير للحصول على عرض مقرب للمستند";

                case PrintingStringId.ClosePreview:
                    return "إغلاق";
                case PrintingStringId.SendFile_Hint:
                    return "قم بتصدير المستند بأحد التنسيقات المتاحة وأرفقه برسالة بريد إلكتروني";
                case PrintingStringId.RibbonPageGroup_Navigation:
                    return "التنقل بين الصفحات";
                case PrintingStringId.RibbonPageGroup_View:
                    return "العرض";
                case PrintingStringId.RibbonPageCaption:
                    return "معاينة";
                case PrintingStringId.RibbonPageGroup_File:
                    return "ملف";
                case PrintingStringId.RibbonPageGroup_Print:
                    return "الطباعة";
                case PrintingStringId.RibbonPageGroup_Zoom:
                    return "التكبير والتصغير";
                case PrintingStringId.RibbonPageGroup_Export:
                    return "التصدير";
                case PrintingStringId.ToolBarCaption:
                    return "معاينةالطابعة";
                case PrintingStringId.Print_Hint:
                    return "حدد إعدادات الصفحة واطبع المستند";
                case PrintingStringId.Save_Hint:
                    return "أحفظ المستند بالجهاز أو بذاكرة أخري";

                case PrintingStringId.PrintDirect:
                    return "طباعة مباشرة";

                case PrintingStringId.PageSetup:
                    return "إعداد الصفحة";

                case PrintingStringId.Zoom:
                    return "تكبير/تصغير";

                case PrintingStringId.FirstPage:
                    return "الصفحة الأولى";
                case PrintingStringId.FirstPage_Hint:
                    return "انتقل إلى الصفحة الأولى من التقرير";
                case PrintingStringId.LastPage:
                    return "الصفحة الأخيرة";
                case PrintingStringId.LastPage_Hint:
                    return "انتقل إلى الصفحةالأخيرة من التقرير";
                case PrintingStringId.NextPage:
                    return "التالي";
                case PrintingStringId.NextPage_Hint:
                    return "انتقل إلى الصفحةالتالية من التقرير";
                case PrintingStringId.PreviousPage:
                    return "السابق";
                case PrintingStringId.PreviousPage_Hint:
                    return "انتقل إلى الصفحةالسابقة من التقرير";
                case PrintingStringId.ExportFile:
                    return "تصدير";
                case PrintingStringId.ExportFile_Hint:
                    return "قم بتصدير المستند بأحد التنسيقات المتاحة واحفظه كملف على القرص";
                case PrintingStringId.Search:
                    return "بحث";

                case PrintingStringId.PageRange:
                    return "نطاق الصفحات";

                case PrintingStringId.AllPages:
                    return "كل الصفحات";

                case PrintingStringId.CurrentPage:
                    return "الصفحة الحالية";

                case PrintingStringId.PrintPreviewWindowCaption:
                    return "معاينة قبل الطباعة";

                case PrintingStringId.OK:
                    return "موافق";

                case PrintingStringId.Cancel:
                    return "إلغاء";

                case PrintingStringId.Error:
                    return "خطأ";

                case PrintingStringId.CurrentPageDisplayFormat:
                    return "صفحة {0} من {1}";
                case PrintingStringId.Watermark:
                    return "علامة مائية";
                case PrintingStringId.SendFile:
                    return "أرسال عبر البريد";
                case PrintingStringId.PageLayout:
                    return "شكل الصفحة";
                case PrintingStringId.DecreaseZoom:
                    return "تصغير";
                case PrintingStringId.IncreaseZoom:
                    return "تكبير";
                case PrintingStringId.Save:
                    return "حفظ";
            }

            return base.GetLocalizedString(id);
        }


    }

}
