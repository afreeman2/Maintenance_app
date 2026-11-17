-- ترحيل لمرة واحدة من القاعدة القديمة على نفس مثيل SQL Server.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() = N'Cont_maint'
    THROW 51000, N'اختر قاعدة وجهة مختلفة عن القاعدة القديمة.', 1;

BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock = sys.sp_getapplock @Resource = N'Maintenance.Setup.Migration',
    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
IF @lock < 0 THROW 51000, N'هناك عملية ترحيل أخرى قيد التنفيذ.', 1;

IF OBJECT_ID(N'dbo.SetupLegacyMigration', N'U') IS NOT NULL
BEGIN
    COMMIT;
    SELECT CAST(1 AS bit) AS AlreadyMigrated, Orders, AuditRows, ArchivedUsers,
           MultiServiceOrders, ArchivedParts, ArchivedDetails FROM dbo.SetupLegacyMigration;
    RETURN;
END;

IF DB_ID(N'Cont_maint') IS NULL
    THROW 51000, N'قاعدة Cont_maint غير موجودة أو غير متاحة على الخادم المحدد.', 1;

-- ترتيب النقل يحترم المفاتيح الخارجية ويُبقي جميع القيود فعّالة.
DECLARE @tables table (Position int IDENTITY PRIMARY KEY, Name sysname);
INSERT @tables (Name) VALUES
(N'T_Branch'), (N'T_Locations'), (N'T_Build'), (N'T_Service'), (N'T_Employe'),
(N'T_Manage'), (N'T_Section'), (N'T_SystemUser'), (N'T_Maintenance_request_Users'),
(N'T_Maintenance_Request'), (N'T_Mangment_Request'), (N'T_ManNotifs'), (N'T_ManNofiusers'),
(N'T_Notifi'), (N'T_Notif_Action'), (N'T_Notif_Action_Users'), (N'T_MaintenanceCosts'),
(N'T_Emergency_MaintenanceCosts'), (N'T_Emergency_MaintenanceCosts_Services'),
(N'T_MaintenanceCosts_Parts'), (N'T_Emp_Maint'), (N'T_FinishedMaintenance'), (N'T_Observer');

DECLARE @name sysname, @sql nvarchar(max), @message nvarchar(2048), @i int = 1;
DECLARE @archiveParts sysname = N'Backup_OrphanParts_20261030',
        @archiveDetails sysname = N'Backup_OrphanServices_20261030';
DECLARE @tableCount int = (SELECT COUNT(*) FROM @tables);

-- لا ندمج مع بيانات موجودة. نمسك قفل كل جدول طوال المعاملة لمنع الكتابة بالتزامن.
WHILE @i <= @tableCount + 2
BEGIN
    SET @name = CASE @i - @tableCount WHEN 1 THEN @archiveParts WHEN 2 THEN @archiveDetails
                ELSE (SELECT Name FROM @tables WHERE Position = @i) END;
    IF OBJECT_ID(N'dbo.' + @name, N'U') IS NULL
        OR (@i <= (SELECT COUNT(*) FROM @tables)
            AND OBJECT_ID(N'Cont_maint.dbo.' + @name, N'U') IS NULL)
    BEGIN
        SET @message = N'جدول مطلوب غير موجود في إحدى القاعدتين: ' + @name;
        THROW 51000, @message, 1;
    END;
    SET @message = N'قاعدة الوجهة تحتوي على بيانات في الجدول ' + @name + N'.';
    SET @sql = N'IF EXISTS (SELECT 1 FROM dbo.' + QUOTENAME(@name)
             + N' WITH (TABLOCKX, HOLDLOCK)) THROW 51000, @message, 1;';
    EXEC sys.sp_executesql @sql, N'@message nvarchar(2048)', @message;
    SET @i += 1;
END;

-- مفتاح التكليف مركب، ويحدد رقم البند ID ترتيب بنود التكليف القديم.
SELECT d.ID, d.Location COLLATE DATABASE_DEFAULT AS Location, d.Location_ID, d.Branch_ID,
       d.Emergency_MaintenanceCosts_No, d.Serv_num,
       ROW_NUMBER() OVER (PARTITION BY d.Location COLLATE DATABASE_DEFAULT, d.Location_ID,
           d.Branch_ID, d.Emergency_MaintenanceCosts_No ORDER BY d.ID) AS ItemOrder
INTO #Services
FROM Cont_maint.dbo.T_Emergency_MaintenanceCosts_Services AS d;
CREATE UNIQUE CLUSTERED INDEX IX_FirstService
    ON #Services (Location, Location_ID, Branch_ID, Emergency_MaintenanceCosts_No, ItemOrder);

SELECT Location, Location_ID, Branch_ID, Emergency_MaintenanceCosts_No
INTO #MultiService
FROM #Services
GROUP BY Location, Location_ID, Branch_ID, Emergency_MaintenanceCosts_No
HAVING COUNT(DISTINCT Serv_num) > 1;

CREATE TABLE dbo.SetupLegacyServiceTypes
(
    DetailID bigint NOT NULL PRIMARY KEY, Location nvarchar(200) NOT NULL,
    Location_ID smallint NOT NULL, Branch_ID smallint NOT NULL,
    OrderNo bigint NOT NULL, Serv_num smallint NOT NULL, IsSelected bit NOT NULL,
    HasOrder bit NOT NULL
);
-- نوثّق كل بند من التكليفات متعددة الأنواع، ونحفظ النوع الأصلي للبنود اليتيمة أيضًا.
INSERT dbo.SetupLegacyServiceTypes
SELECT d.ID, d.Location, d.Location_ID, d.Branch_ID, d.Emergency_MaintenanceCosts_No,
       d.Serv_num, IIF(h.Emergency_MaintenanceCosts_No IS NOT NULL AND d.ItemOrder = 1, 1, 0),
       IIF(h.Emergency_MaintenanceCosts_No IS NULL, 0, 1)
FROM #Services AS d
LEFT JOIN Cont_maint.dbo.T_Emergency_MaintenanceCosts AS h
  ON h.Location COLLATE DATABASE_DEFAULT = d.Location AND h.Location_ID = d.Location_ID
 AND h.Branch_ID = d.Branch_ID AND h.Emergency_MaintenanceCosts_No = d.Emergency_MaintenanceCosts_No
LEFT JOIN #MultiService AS m
  ON m.Location = d.Location AND m.Location_ID = d.Location_ID AND m.Branch_ID = d.Branch_ID
 AND m.Emergency_MaintenanceCosts_No = d.Emergency_MaintenanceCosts_No
WHERE m.Emergency_MaintenanceCosts_No IS NOT NULL OR h.Emergency_MaintenanceCosts_No IS NULL;

-- نحفظ معرف الموظف المفقود ونترك علاقته الاختيارية فارغة حتى يمكن استعادتها عند توفر بياناته.
SELECT h.Location COLLATE DATABASE_DEFAULT AS Location, h.Location_ID, h.Branch_ID,
       h.Emergency_MaintenanceCosts_No AS OrderNo, h.Employe_ID
INTO dbo.SetupLegacyMissingEmployees
FROM Cont_maint.dbo.T_Emergency_MaintenanceCosts AS h
WHERE h.Employe_ID IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM Cont_maint.dbo.T_Employe AS e
     WHERE e.Location=h.Location AND e.Location_ID=h.Location_ID AND e.Branch_ID=h.Branch_ID
       AND e.Employe_ID=h.Employe_ID);

DECLARE @archivedUsers bigint = 0, @archivedParts bigint = 0, @archivedDetails bigint = 0;
DECLARE @columns nvarchar(max), @values nvarchar(max), @join nvarchar(max), @filter nvarchar(max),
        @target nvarchar(300), @source nvarchar(300), @archive nvarchar(300),
        @identity bit, @actual bigint, @expected bigint, @archived bigint;
SET @i = 1;
WHILE @i <= (SELECT COUNT(*) FROM @tables)
BEGIN
    SELECT @name = Name FROM @tables WHERE Position = @i;
    SET @target = N'dbo.' + QUOTENAME(@name);
    SET @source = N'Cont_maint.dbo.' + QUOTENAME(@name);
    SET @join = N'';
    SET @filter = N'';
    SET @archive = NULL;
    SET @archived = 0;

    -- أسماء أعمدة الوجهة هي المرجع. عمودا النوع والمستخدم لهما تحويل صريح.
    SELECT @columns = STRING_AGG(CONVERT(nvarchar(max), QUOTENAME(c.name)), N',')
                      WITHIN GROUP (ORDER BY c.column_id),
           @values = STRING_AGG(CONVERT(nvarchar(max),
                     CASE WHEN @name = N'T_Emergency_MaintenanceCosts' AND c.name = N'Serv_num' THEN N'd.Serv_num'
                          WHEN @name = N'T_Emergency_MaintenanceCosts' AND c.name = N'Employe_ID' THEN N'e.Employe_ID'
                          WHEN @name = N'T_Observer' AND c.name = N'User_id' THEN N'u.User_ID'
                          ELSE N's.' + QUOTENAME(c.name) END), N',')
                     WITHIN GROUP (ORDER BY c.column_id),
           @identity = CONVERT(bit, MAX(CONVERT(int, c.is_identity)))
    FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(@target) AND c.is_computed = 0 AND c.system_type_id <> 189;

    IF @name = N'T_Emergency_MaintenanceCosts'
        SET @join = N' LEFT JOIN #Services AS d ON d.ItemOrder=1
            AND d.Location=s.Location COLLATE DATABASE_DEFAULT AND d.Location_ID=s.Location_ID
            AND d.Branch_ID=s.Branch_ID AND d.Emergency_MaintenanceCosts_No=s.Emergency_MaintenanceCosts_No
            LEFT JOIN dbo.T_Employe AS e ON e.Location=s.Location COLLATE DATABASE_DEFAULT
            AND e.Location_ID=s.Location_ID AND e.Branch_ID=s.Branch_ID AND e.Employe_ID=s.Employe_ID';

    IF @name = N'T_Observer'
    BEGIN
        -- نرفض الالتباس بدل إسناد حركة التدقيق إلى مستخدم غير صحيح.
        IF EXISTS (SELECT 1 FROM dbo.T_SystemUser GROUP BY TRIM(User_Name) HAVING COUNT_BIG(*) > 1)
            THROW 51000, N'أسماء مستخدمين متطابقة في المصدر تمنع ربط سجل التدقيق.', 1;

        ;WITH AuditNames AS
        (
            SELECT DISTINCT TRIM(User_Nam) COLLATE DATABASE_DEFAULT AS UserName
            FROM Cont_maint.dbo.T_Observer
        )
        INSERT dbo.T_SystemUser
            (User_Name, PassWord, Date_Modify, st, pass_st, fordon, IsSound,
             formaint, foremp, forservices, forbuild, forlocation, mang_check, foruser,
             formangreq, branch_report, formaintrepo)
        SELECT a.UserName, CONVERT(nvarchar(64), CRYPT_GEN_RANDOM(32), 2), CONVERT(date, SYSUTCDATETIME()),
               0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        FROM AuditNames AS a
        WHERE NOT EXISTS (SELECT 1 FROM dbo.T_SystemUser AS u WHERE TRIM(u.User_Name) = a.UserName);
        SET @archivedUsers = @@ROWCOUNT;
        SET @join = N' LEFT JOIN dbo.T_SystemUser AS u
            ON TRIM(u.User_Name)=TRIM(s.User_Nam) COLLATE DATABASE_DEFAULT';
    END;

    IF @name IN (N'T_Emergency_MaintenanceCosts_Services', N'T_MaintenanceCosts_Parts')
    BEGIN
        SET @archive = CASE @name WHEN N'T_MaintenanceCosts_Parts' THEN @archiveParts ELSE @archiveDetails END;
        SET @archive = N'dbo.' + QUOTENAME(@archive);
        SET @filter = N' EXISTS (SELECT 1 FROM dbo.T_Emergency_MaintenanceCosts AS h
            WHERE h.Location=s.Location COLLATE DATABASE_DEFAULT AND h.Location_ID=s.Location_ID
            AND h.Branch_ID=s.Branch_ID AND h.Emergency_MaintenanceCosts_No=s.'
            + CASE @name WHEN N'T_MaintenanceCosts_Parts' THEN N'MaintenanceCosts_No'
              ELSE N'Emergency_MaintenanceCosts_No' END + N')';
        -- السجلات التي لا رأس لها تبقى محفوظة في جدولي الأرشيف المعتمدين في الوجهة.
        SET @sql = N'SET IDENTITY_INSERT ' + @archive + N' ON;
            INSERT ' + @archive + N' (' + @columns + N') SELECT ' + @values + N' FROM '
            + @source + N' AS s WHERE NOT ' + @filter + N';
            SET @archived = ROWCOUNT_BIG(); SET IDENTITY_INSERT ' + @archive + N' OFF;';
        EXEC sys.sp_executesql @sql, N'@archived bigint OUTPUT', @archived OUTPUT;
        IF @name = N'T_MaintenanceCosts_Parts' SET @archivedParts = @archived;
        ELSE SET @archivedDetails = @archived;
        SET @filter = N' WHERE ' + @filter;
    END;

    -- نجلب كل أعمدة الوجهة القابلة للكتابة، وأي اختلاف غير مغطى يوقف المعاملة.
    SET @sql = N'SELECT @expected=COUNT_BIG(*) FROM ' + @source + N';'
        + CASE WHEN @identity = 1 THEN N'SET IDENTITY_INSERT ' + @target + N' ON;' ELSE N'' END
        + N'INSERT ' + @target + N' (' + @columns + N') SELECT ' + @values + N' FROM '
        + @source + N' AS s' + @join + @filter + N'; SET @actual=ROWCOUNT_BIG();'
        + CASE WHEN @identity = 1 THEN N'SET IDENTITY_INSERT ' + @target + N' OFF;' ELSE N'' END;
    EXEC sys.sp_executesql @sql, N'@expected bigint OUTPUT, @actual bigint OUTPUT',
         @expected OUTPUT, @actual OUTPUT;
    IF @actual + @archived <> @expected
    BEGIN
        SET @message = N'عدد السجلات المنقولة لا يطابق المصدر في ' + @name + N'.';
        THROW 51000, @message, 1;
    END;
    SET @i += 1;
END;

-- لا تظهر علامة الاكتمال إلا بعد نجاح نقل البيانات كلها.
CREATE TABLE dbo.SetupLegacyMigration
(
    CompletedAt datetime2(0) NOT NULL, Orders bigint NOT NULL, AuditRows bigint NOT NULL,
    ArchivedUsers bigint NOT NULL, MultiServiceOrders bigint NOT NULL,
    ArchivedParts bigint NOT NULL, ArchivedDetails bigint NOT NULL
);
INSERT dbo.SetupLegacyMigration
SELECT SYSUTCDATETIME(), (SELECT COUNT_BIG(*) FROM dbo.T_Emergency_MaintenanceCosts),
    (SELECT COUNT_BIG(*) FROM dbo.T_Observer), @archivedUsers,
    (SELECT COUNT_BIG(*) FROM #MultiService AS m WHERE EXISTS
        (SELECT 1 FROM dbo.T_Emergency_MaintenanceCosts AS h
         WHERE h.Location=m.Location AND h.Location_ID=m.Location_ID AND h.Branch_ID=m.Branch_ID
           AND h.Emergency_MaintenanceCosts_No=m.Emergency_MaintenanceCosts_No)),
    @archivedParts, @archivedDetails;
COMMIT;
SELECT CAST(0 AS bit) AS AlreadyMigrated, Orders, AuditRows, ArchivedUsers,
       MultiServiceOrders, ArchivedParts, ArchivedDetails FROM dbo.SetupLegacyMigration;
