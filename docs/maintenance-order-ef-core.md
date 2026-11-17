# Original maintenance order behavior ported to EF Core

Source reviewed: `D:\GitHub\Maintenance_services\Maintenance_services\Emergency_Mainte_Form.xaml.cs`.

The original source file was not edited. SHA-256: `CAE68E462D47F2E7655F53023AB7722EC1816FF8DC4EB8BA65FEA3BFF3B75AFA`.

The DevExpress window retains the original operation names and workflow in `maintenance_Order.xaml.cs`. `MaintenanceOrderEfStore.cs` replaces LINQ-to-SQL contexts and SubmitChanges with short-lived EF Core contexts and explicit transactions.

## تبسيط كود الواجهة — 2026-11-01

متابعة تنظيم الكود في اليوم نفسه: وُضعت المجموعات المترابطة داخل `#region` و`#endregion` بعناوين عربية؛ 20 منطقة في `maintenance_Order.xaml.cs` و11 في `MaintenanceOrderEfStore.cs` و5 في `MaintenanceOrderState.cs`. بقي ترتيب الدوال والحقول والتعليقات كما هو. نجح بناء Debug/x64 بالتحذيرات السابقة، وفحص Roslyn لصحة التوجيهات وتطابق رموز C# التنفيذية قبل التنظيم وبعده. لم تُعَد فحوص التشغيل لهذا التغيير التنظيمي ولم تستخدم قاعدة العمل. النسخة السابقة في `artifacts/order-code-before-regions/`، والسجلان `artifacts/order-regions-token-check.log` و`artifacts/order-regions-build.log`. اعتُمد تنظيم مجموعات C# بمناطق عربية ضمن الدليل العام وAGENTS.md.

متابعة التعليقات التوضيحية في اليوم نفسه: أُضيف شرح عربي داخل `maintenance_Order.xaml.cs` و`MaintenanceOrderEfStore.cs` و`MaintenanceOrderState.cs` لأهداف الدوال والأحداث، وحالة النافذة، وقراءة القوائم والفنيين، والمفاتيح المركبة، ومعاملات الحفظ والترقيم، والبحث، والتراجع عند الفشل، وربط الجداول والإجماليات. ثبت فحص Roslyn تطابق رموز C# التنفيذية قبل التعديل وبعده في الملفات الثلاثة؛ التغيير تعليقات فقط. نجح بناء Debug/x64 مع التحذيرات السابقة، ولم تُعَد فحوص التشغيل أو تُستخدم قاعدة عمل لهذه الإضافة. السجلان: `artifacts/order-comments-token-check.log` و`artifacts/order-comments-build.log`، ونسخة ما قبل التعليقات في `artifacts/order-code-before-comments/`. اعتُمدت التعليقات العربية تفضيلًا عامًا في تعليمات المستخدم الدائمة.

بناءً على طلب المستخدم، جُمعت أحداث الواجهة في `maintenance_Order.xaml.cs` وأُلغي الملف الجزئي `maintenance_Order.EfCore.cs` بعد نقل محتواه. لكل زر دالة عادية واضحة تحتوي `try/catch/finally`، مع تعليقات عربية وقراءة صريحة للخانات والفنيين. أزيل تمرير عمليات الأزرار كدوال مجهولة إلى `RunOperationAsync`، وأصبحت `BeginOperation` و`EndOperation` دالتين عاديتين لمنع تكرار العملية وإعادة تفعيل الواجهة.

خريطة القراءة:

- `Insert_Click`، `Edite_button_Click`، `delete_button_Click`: إدراج التكليف وتعديله وحذفه.
- `ReadDraft`: قراءة خانات الرأس والفنيين والتحقق منها.
- `Add_ToList_Click` و`Update_Button_Click`: بنود بيان الصيانة.
- `Add_To_Prt_List_Click` و`Update_Parts_Button_Click`: المواد وإجمالياتها.
- `show_emn` و`SearchAsync` و`LoadOrderAsync`: البحث وعرض التكليف.
- `Print_Button_Click`: تحميل التقرير بالقوالب الأصلية.

بقيت معاملات EF Core وقواعد البيانات في `MaintenanceOrderEfStore.cs`، وقوائم الربط في `MaintenanceOrderState.cs`. شكل XAML وأسماء الأدوات وربطها ومنطق الحفظ الأصلي محفوظة. نسخة الكود السابقة في `artifacts/order-code-before-traditional/`.

التحقق بعد التبسيط: نجح بناء Debug/x64 مع التحذيرات السابقة. نجح فحص WPF الفعلي عند خمسة مقاسات، واستُخدمت أحداث أزرار إضافة البيان والمادة بدل استدعاء دوال الإضافة مباشرة، بما يشمل رفض المدخلات الناقصة واستعادة الواجهة ثم التعديل والتفريغ. نجحت اختبارات EF Core على SQLite المعزولة بما فيها المعاملات والتراجع وقيود الإنجاز والعزل ودقة الكسور وتوليد تقريري RDLC. لا كتابة على قاعدة العمل ولا اختبار طابعة فعلية.

سجلات التحقق: `artifacts/order-traditional-build.log` و`artifacts/order-traditional-ui.log` و`artifacts/order-traditional-ef.log`. نسخة التشغيل: `Maintenance_app/bin/x64/Debug/net10.0-windows/Maintenance_app.exe`.

Preserved operations:

- User + current year + five-digit sequential order numbering, recomputed within the insert transaction.
- Header, technician selection, service details, materials, quantities and prices.
- Insert, edit, delete, clear, direct number lookup and advanced date/technician/service search.
- Immediate detail/material add, edit and delete when an existing order is open; draft-only edits before the order is saved.
- Finished-order restrictions and delete confirmations.
- Both maintenance-notification records and linked-request state transitions, committed atomically with the order.
- Original single- and multiple-technician RDLC templates, unchanged, with their original DataSet1/DataSet2 field names and report viewer printing/export.
- EF-backed add dialogs for building, management and department lookup buttons.

Schema adaptation: the generated current EF model stores `Serv_num` on the parent order. The original database access placed it on service details. Composite location/location ID/branch/order keys are used consistently. Updating an existing header retains its original User_ID and Date_Modify, as the source did.

Validation:

- Production project builds for net10.0-windows/x64.
- `tests/MaintenanceOrderEfTests` uses an isolated in-memory SQLite relational database. The test context supplies identity values only to emulate SQL Server identities on composite keys. It exercises insert/update/delete, per-line CRUD, numbering, decimal precision, both notification tables, request linking/reset, full rollback after a late failure, finished-order guards and composite-key isolation. Both original report templates render to PDF from EF-loaded records.
- `tests/MaintenanceOrderPreview` checks the real WPF window at five viewport sizes, stacked sections, add/edit/reset, totals and the DevExpress header automation regression. `LaunchProbe` checks main buttons and F7 with real application resources; `PalettePreview` renders the actual main and login content.

No production database writes are used for validation. SQL Server credentials are read only from the application's existing local configuration and are never logged.
