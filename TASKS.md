# TASKS.md — Requests Management (מבדק Full Stack, רמה ג')

| | |
|---|---|
| מקור הדרישות המחייב | `מבחן_FULL_STACK_.pdf` (סעיפים 1–11, אופן הגשה, אופן הערכה) |
| מועד הגשה | 28.10.2026 |
| תקציב זמן | 6 ימי עבודה × **מקסימום 2 שעות** ביום (12 שעות). ביום ההגשה (28.10) — אימות והגשה בלבד |
| Stack | ASP.NET Core 8 Web API · EF Core 8 · SQL Server · Angular 20 · xUnit |
| סטטוס התכנון | Updated 22.10: Backend complete + Angular list/filter done + 3 bugs fixed; revised schedule in section 6 |

**מקרא**
- **עדיפות:** `P1` = חובה, ליבת הניקוד, לא נחתכת · `P2` = חובה/מחזק ציון, ניתן לצמצם היקף · `P3` = רשות (Stretch), רק אם נשאר זמן.
- **סטטוס:** `To Do` / `In Progress` / `Done`.
- **הערכות זמן** הן יעדים בעבודה עם AI, כולל זמן לקריאה ובדיקה ידנית של כל קוד שנוצר. הזמן בפועל נרשם בסוף כל יום.
- **כלל Seeder:** קוד ה-Seeder יסמן בבירור את ה-DB שהוא מוחק. לא מוחקים נתונים ללא אישור מפורש.
- **כלל Bulk:** ישויות שנכשלו ב-Concurrency יוסרו מ-ChangeTracker לפני ה-`SaveChanges` הבא, כדי למנוע retry בשוגג.
- **כלל Angular:** מתחילים Front-End ברגע ש-GET /api/requests עובד (לא מחכים לסוף).
- **כלל תלויות:** שמירה על ארכיטקטורה פשוטה — לא מוסיפים תלויות ללא נימוק.

---

## 1. ניתוח דרישות ו-Traceability

כל דרישה מה-PDF קיבלה מזהה `R-x.y` ומקושרת למשימה אחת או יותר.

### 1.0 מודל נתונים ונתוני בדיקה (סעיף "המשימה" + "מבנה פנייה")

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-0.1 | ישות פנייה: Id, Title, OrganizationName, Status (New/InProgress/Waiting/Completed), Priority (Low/Medium/High), AssignedTo, CreatedAt, UpdatedAt, RowVersion | T-102 | P1 |
| R-0.2 | לפחות 100,000 רשומות | T-104 | P1 |
| R-0.3 | אפשר ליצור את נתוני הבדיקה מחדש (Reproducible) | T-104, T-901 | P1 |
| R-0.4 | בחירת DB (SQL Server / MongoDB / אחר) ונימוק | T-902, §3 | P1 |
| R-0.5 | מספר משתמשים עובדים במקביל, כמות נתונים גדולה (הקשר לביצועים ולתחרותיות) | T-302, T-401, T-801 | P1 |

### 1.1 שליפת נתונים — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-1.1 | REST API ב-.NET לשליפת פניות, בלי להעביר את כל הרשומות ל-Client | T-202, T-203 | P1 |
| R-1.2 | Pagination בצד השרת | T-202 | P1 |
| R-1.3 | סינון משולב: Status, Priority, OrganizationName, AssignedTo, טווח תאריכים | T-201, T-202 | P1 |
| R-1.4 | חיפוש טקסטואלי לפחות ב-Title וב-OrganizationName | T-202 | P1 |
| R-1.5 | מיון לפי 3 שדות לפחות, Asc/Desc | T-201, T-202 | P1 |
| R-1.6 | לפחות 2 Aggregations רלוונטיים | T-204 | P1 |
| R-1.7 | הגבלת Page Size ו-Validation לפרמטרים | T-201 | P1 |
| R-1.8 | CancellationToken בפעולות I/O | T-202, T-204, T-302, T-401 | P1 |
| R-1.9 | החזרת DTOs ולא ישויות DB | T-201, T-202 | P1 |
| R-1.10 | תיעוד: איך נמנעת טעינת כל הנתונים לזיכרון ואיך האינדקסים תומכים בחיפוש ובסינון | T-801, T-902 | P1 |

### 1.2 עדכון סטטוס ותחרותיות — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-2.1 | פנייה שלא קיימת → קוד HTTP מתאים (404) | T-302 | P1 |
| R-2.2 | סטטוס לא חוקי / מעבר אסור → Validation ברור (400) | T-301, T-302 | P1 |
| R-2.3 | Optimistic Concurrency: מבין עדכונים מתחרים רק אחד מצליח, והשני מקבל 409 Conflict | T-102, T-302 | P1 |
| R-2.4 | UpdatedAt ו-RowVersion מתעדכנים באופן עקבי | T-302 | P1 |
| R-2.5 | מניעת Lost Update | T-302, T-401 | P1 |

### 1.3 היסטוריית שינויים — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-3.1 | Audit לכל שינוי Status: RequestId, PreviousStatus, NewStatus, ChangedAt, ChangedBy | T-102, T-302, T-401 | P1 |
| R-3.2 | Endpoint לצפייה בהיסטוריית השינויים של פנייה | T-303 | P1 |

### 1.4 פעולת Bulk — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-4.1 | Endpoint לעדכון סטטוס של עד 100 פניות בבקשה אחת | T-401 | P1 |
| R-4.2 | הגדרה ותיעוד של ההתנהגות כשחלק מהפניות לא קיימות או נכשלות בגלל Concurrency | T-401, T-902 | P1 |
| R-4.3 | בחירה בין All-or-Nothing ל-Partial Success, עם נימוק ותשובה ברורה ללקוח | T-401, T-902 | P1 |

### 1.5 Angular — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-5.1 | חיפוש, סינון, מיון ודפדוף בצד השרת | T-702 | P1 |
| R-5.2 | Debounce לחיפוש | T-702 | P1 |
| R-5.3 | ביטול או החלפה של בקשות קודמות (switchMap) | T-702 | P1 |
| R-5.4 | מצבי Loading, Empty ו-Error | T-702 | P1 |
| R-5.5 | עדכון סטטוס וטיפול ב-409 Conflict | T-704 | P1 |
| R-5.6 | הצגת Aggregations | T-703 | P1 |
| R-5.7 | צפייה בהיסטוריית שינויים | T-705 | P1 |
| R-5.8 | הפרדת אחריות בין Components ל-Services | T-701–T-705 | P1 |

### 1.6 ביצועים — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-6.1 | לפחות 2 פעולות API מרכזיות נבחנות על 100,000 רשומות, ומתועדות | T-801 | P1 |
| R-6.2 | Execution/Query Plan או מדד מקביל אחד לפחות | T-801 | P1 |
| R-6.3 | פירוט האינדקסים שנוספו והסיבה לכל אחד | T-103, T-801 | P1 |
| R-6.4 | זיהוי Bottleneck אפשרי והצעה לשיפור | T-801 | P1 |
| R-6.5 | אין טעינה מלאה לפני Pagination/Filtering | T-202 | P1 |

### 1.7 Cache — חובה ברמה מוגבלת

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-7.1 | Cache לנתון מסכם אחד לפחות (או תרחיש קריאה מוצדק) | T-501 | P1 |
| R-7.2 | Expiration מוגדר | T-501 | P1 |
| R-7.3 | Invalidation אחרי עדכון | T-501 | P1 |
| R-7.4 | נימוק לבחירת המידע שנשמר ב-Cache | T-902 | P1 |
| R-7.5 | תיאור ההתאמה הנדרשת כשיש כמה מופעי שרת | T-902 | P1 |

### 1.8 בדיקות אוטומטיות — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-8.1 | לפחות 4 בדיקות משמעותיות, ולפחות אחת מהן Integration | T-602–T-604 | P1 |
| R-8.2 | תרחיש תקין של שליפה/סינון או עדכון | T-603 | P1 |
| R-8.3 | Validation / קלט לא תקין | T-602, T-605 | P1 |
| R-8.4 | Concurrency Conflict | T-604 | P1 |
| R-8.5 | Integration Test שכולל API ו-DB (או תחליף מתאים) | T-601, T-603, T-604 | P1 |

### 1.9 תכנון עבודה וחלוקה למשימות — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-9.1 | חלוקה ליחידות עבודה (Epic / Task) | T-000 (המסמך הזה, §4–§5) | P1 |
| R-9.2 | לכל משימה: מה נדרש ומה התוצר | §5 | P1 |
| R-9.3 | סדר ביצוע ותלויות | §4, §6 | P1 |
| R-9.4 | הערכת מאמץ | §4, §7 | P1 |
| R-9.5 | קשר ברור בין דרישות למשימות | §1 (הטבלה הזו) | P1 |
| R-9.6 | גם משימות שאינן קוד: תכנון DB, בדיקות, ביצועים, תיעוד, היערכות להרצה | T-103, T-6xx, T-801, T-9xx | P1 |

### 1.10 איכות הקוד

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-10.1 | מבנה פרויקט ברור והפרדת אחריות | T-101, §2 | P1 |
| R-10.2 | קוד קריא, Naming עקבי, בלי שכפול | כל המשימות | P1 |
| R-10.3 | DTOs / Models / Mapping ברורים | T-201 | P1 |
| R-10.4 | DI ושימוש נכון ב-Lifetimes | T-101 | P1 |
| R-10.5 | עבודה אסינכרונית נכונה | כל משימות ה-Backend | P1 |
| R-10.6 | Global Error Handling / ProblemDetails | T-304 | P1 |
| R-10.7 | Validation עקבי | T-201, T-304 | P1 |
| R-10.8 | Logging בנקודות משמעותיות, בלי חשיפת מידע רגיש | T-304, T-302, T-401 | P1 |
| R-10.9 | הימנעות מ-Over-Engineering | §2, §3 | P1 |

### 1.11 תיעוד הפתרון — חובה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-11.1 | הוראות הבנה והפעלה | T-901 | P1 |
| R-11.2 | טכנולוגיות וגרסאות | T-901 | P1 |
| R-11.3 | תיאור מבנה הפתרון | T-901 | P1 |
| R-11.4 | בחירת DB ואינדקסים | T-902 | P1 |
| R-11.5 | הסבר Concurrency ו-Bulk Update | T-902 | P1 |
| R-11.6 | הסבר Cache ו-Invalidation | T-902 | P1 |
| R-11.7 | שתי החלטות טכנולוגיות משמעותיות + חלופות שנשקלו | T-902 | P1 |
| R-11.8 | מגבלה ידועה אחת ודבר אחד שאפשר היה לשפר | T-902 | P1 |
| R-11.9 | פירוט השימוש בכלי AI: באילו חלקים, ומה נבדק או שונה ידנית | T-902 (רישום שוטף לאורך כל הימים) | P1 |

### 1.12 אופן הגשה

| ID | דרישה | משימות | עדיפות |
|---|---|---|---|
| R-12.1 | הבודקים יכולים לבחון בנוחות את המערכת, הקוד, התכנון, המימוש וההחלטות | T-903 | P1 |
| R-12.2 | ההגשה כוללת את כל הרכיבים ומאפשרת הרצה | T-901, T-903 | P1 |
| R-12.3 | בלי ZIP ובלי קוד מצורף לדוא"ל → Repository ב-GitHub עם גישה לבודקים | T-101, T-903 | P1 |

### 1.13 הנחות לגבי פערים באפיון (יתועדו ב-README)

| נושא | ה-PDF לא מגדיר | ההנחה שלנו |
|---|---|---|
| מעברי סטטוס | אילו מעברים מותרים | `New→InProgress/Waiting`, `InProgress→Waiting/Completed`, `Waiting→InProgress/Completed`. `Completed` הוא סטטוס סופי. מעבר לאותו סטטוס אסור |
| ChangedBy | אין דרישה ל-Authentication | שדה `changedBy` חובה בגוף הבקשה (עם Validation). ב-UI יש שדה "משתמש נוכחי". זו מגבלה ידועה ותתועד |
| טווח תאריכים | על איזה שדה | `CreatedAt` (`createdFrom` / `createdTo`) |
| סינון OrganizationName לעומת חיפוש | ההבדל ביניהם | סינון = StartsWith (תומך באינדקס). חיפוש חופשי = Contains על Title + OrganizationName |
| Aggregations | אילו בדיוק | ספירה לפי Status וספירה לפי Priority (גלובלי, ב-Cache) |
| Page Size | גבול עליון | ברירת מחדל 20, מקסימום 100 |

---

## 2. ארכיטקטורה

```
RequestsManagement/                (GitHub repo)
├─ README.md                       הוראות הרצה, גרסאות, מבנה
├─ TASKS.md                        המסמך הזה
├─ docs/
│  ├─ decisions.md                 החלטות טכנולוגיות, Concurrency, Bulk, Cache, AI usage
│  └─ performance.md               מדידות, Plans, אינדקסים, Bottlenecks
├─ backend/
│  ├─ RequestsManagement.sln
│  ├─ src/RequestsManagement.Api/
│  │  ├─ Controllers/              RequestsController (HTTP בלבד: Binding, Mapping תוצאה → קוד HTTP)
│  │  ├─ Services/                 RequestQueryService, RequestStatusService, RequestSummaryService
│  │  ├─ Domain/                   Request, RequestStatusHistory, enums, StatusTransitionPolicy
│  │  ├─ Data/                     AppDbContext, Configurations/, Migrations/, Seeding/DataSeeder
│  │  ├─ Dtos/                     Request/Response DTOs + Mapping (Projection ב-LINQ, בלי AutoMapper)
│  │  ├─ Infrastructure/           GlobalExceptionHandler (IExceptionHandler + ProblemDetails)
│  │  └─ Program.cs                DI, Middleware, הפעלת Seeder דרך CLI arg
│  └─ tests/RequestsManagement.Tests/
│     ├─ Unit/                     StatusTransitionPolicy, Validation של פרמטרים
│     └─ Integration/              WebApplicationFactory + SQL Server DB ייעודי לבדיקות
└─ frontend/requests-app/          Angular 20, Standalone Components
   └─ src/app/
      ├─ core/                     models (DTO interfaces), RequestsApiService (HttpClient בלבד)
      └─ features/requests/
         ├─ requests-page/         Container: RxJS pipeline (state, debounce, switchMap)
         ├─ requests-filters/      Presentational: טופס סינון/חיפוש → Output
         ├─ requests-table/        Presentational: טבלה, מיון, דפדוף, בחירת סטטוס
         ├─ summary-panel/         Aggregations
         └─ request-history/       היסטוריית שינויים
```

**הפרדת אחריות**
- **Controller**: מקבל DTO, מעביר CancellationToken, וממפה את תוצאת ה-Service לקוד HTTP. אין בו לוגיקה עסקית.
- **Service**: לוגיקה עסקית, מעברי סטטוס, Concurrency, Audit ו-Cache. מחזיר אובייקט תוצאה (`Success` / `NotFound` / `InvalidTransition` / `Conflict`) ולא זורק Exceptions כדי לנהל זרימה רגילה.
- **Data**: `AppDbContext` ו-Fluent Configurations. בלי Repository Pattern מעל EF Core, כי `DbContext` כבר משמש Unit of Work ו-Repository.
- **DTOs**: חוזה ה-API. ישויות לא חוזרות החוצה. שליפות משתמשות ב-Projection (`Select` ל-DTO), כך שרק העמודות הנחוצות נקראות.
- **Angular**: Service אחראי רק על HTTP. ה-Page Component מחזיק את ה-State ואת ה-Pipeline. רכיבי התצוגה מקבלים Inputs ומחזירים Outputs.

**DI Lifetimes**: `AppDbContext` ו-Services ב-Scoped. `IMemoryCache` ו-`TimeProvider` ב-Singleton. Service שמחזיק DbContext לא נרשם כ-Singleton.

**API Contract (טיוטה)**

| Method | Route | תיאור | קודים |
|---|---|---|---|
| GET | `/api/requests` | רשימה עם `page, pageSize, status[], priority[], organizationName, assignedTo, createdFrom, createdTo, search, sortBy, sortDirection` → `PagedResult<RequestListItemDto>` | 200, 400 |
| GET | `/api/requests/{id}` | פנייה בודדת (כולל `rowVersion`) | 200, 404 |
| GET | `/api/requests/summary` | Aggregations: ספירה לפי Status ולפי Priority (Cached) | 200 |
| PATCH | `/api/requests/{id}/status` | `{ newStatus, rowVersion, changedBy }` | 200, 400, 404, 409 |
| GET | `/api/requests/{id}/history` | היסטוריית סטטוסים, מהחדש לישן | 200, 404 |
| POST | `/api/requests/status/bulk` | `{ newStatus, changedBy, items: [{ id, rowVersion }] }`, עד 100 פריטים | 200 (תוצאה לכל פריט), 400 |

**מה לא נכלל בכוונה:** Microservices, Docker, CQRS/MediatR, AutoMapper, Repository גנרי, Clean Architecture עם 4 פרויקטים, State Management Library ב-Angular (NgRx). כל אלה מוסיפים זמן ומורכבות בלי תועלת בהיקף הזה.

---

## 3. החלטות טכנולוגיות (טיוטה, יורחב ב-`docs/decisions.md`)

### 3.1 SQL Server לעומת MongoDB
- **נבחר: SQL Server.** הנתונים מובנים וסכמתם קבועה. יש תמיכה מובנית ב-`rowversion` ל-Optimistic Concurrency. עדכון הפנייה ושורת ה-Audit נכנסים לאותה טרנזקציה. `GROUP BY` ואינדקסים מורכבים זמינים בקלות. EF Core 8 בשל, וה-SQL Server כבר מותקן בסביבה.
- **חלופה: MongoDB.** הסכמה הגמישה שלו לא נדרשת כאן. Concurrency דורש שדה Version ידני עם `findOneAndUpdate` מותנה. ה-Audit יכול להיות מוטמע במסמך, אבל אז המסמך גדל ללא הגבלה. MongoDB היה עובד, אבל לא מביא יתרון בתרחיש הזה.

### 3.2 Optimistic Concurrency
- **נבחר:** עמודת `rowversion` שמוגדרת `IsRowVersion()` ב-EF. הלקוח שולח את ה-`rowVersion` (Base64) שקיבל. ה-Service טוען את הפנייה, מגדיר `OriginalValue = clientRowVersion`, מעדכן Status ו-UpdatedAt, מוסיף שורת Audit, ומבצע `SaveChanges` אחד (טרנזקציה אחת). אם מתקבל `DbUpdateConcurrencyException`, מוחזר 409 עם הסטטוס וה-rowVersion הנוכחיים ב-ProblemDetails, כדי שה-UI יוכל לרענן.
- **האכיפה היא ב-DB** (`WHERE Id=@id AND RowVersion=@v`) ולא ב-UI בלבד. ה-PDF מדגיש במפורש שאכיפה ב-UI בלבד אינה פתרון מלא.
- **חלופות שנשקלו:** (א) Pessimistic Locking: לא מתאים ל-HTTP Stateless, ומחזיק נעילות בזמן שהמשתמש חושב. (ב) עמודת `int Version` ידנית: עובדת, אבל `rowversion` מתעדכן אוטומטית ב-DB ולא דורש קוד. (ג) Header `If-Match`/ETag: אלגנטי יותר מבחינת REST, אבל ב-Bulk ממילא צריך גרסה לכל פריט בגוף הבקשה, ולכן בחרנו גישה אחידה של שדה בגוף הבקשה.

### 3.3 Bulk Update
- **נבחר: Partial Success.** כל פריט מעודכן ונשמר בנפרד, יחד עם ה-Audit שלו באותה טרנזקציה. התשובה היא 200 עם `results[]`, ולכל פריט `outcome` (`Updated` / `NotFound` / `Conflict` / `InvalidTransition`) ו-`rowVersion` חדש כשהעדכון הצליח, יחד עם סיכום `succeeded` / `failed`.
- **נימוק:** במצב של כמה משתמשים במקביל, Conflict בפנייה אחת מתוך 100 הוא תרחיש סביר. All-or-Nothing היה מכשיל את כל הפעולה בגלל פריט אחד ומאלץ את המשתמש לנסות שוב הכול. ב-Partial Success הלקוח מקבל תמונה מדויקת ויכול לנסות שוב רק את הפריטים שנכשלו.
- **Validation ברמת הבקשה (400):** רשימה ריקה, יותר מ-100 פריטים, Id כפול, `newStatus` לא קיים, `changedBy` חסר.
- **מימוש:** כל הפריטים נטענים בשאילתה אחת (`WHERE Id IN (...)`), ואחר כך יש `SaveChanges` לכל פריט. כלומר עד 100 Round-trips. ה-Bottleneck הזה ידוע ויתועד, והשיפור האפשרי הוא Batch עם טיפול ב-`ex.Entries`.

### 3.4 Cache
- **נבחר:** `IMemoryCache` על `/api/requests/summary`. Absolute Expiration של 60 שניות, ו-Invalidation (`Remove(key)`) אחרי כל עדכון סטטוס מוצלח, בודד או Bulk.
- **נימוק:** ה-Summary זהה לכל המשתמשים, נקרא בכל טעינה של המסך, ודורש `GROUP BY` על 100K שורות. רשימות מסוננות לא נשמרות ב-Cache, כי יש להן הרבה קומבינציות ושיעור Hit נמוך.
- **Race ידוע:** קריאה שהתחילה לפני ה-Commit עלולה לכתוב ערך ישן אחרי ה-Invalidation. ה-TTL מגביל את זמן החשיפה ל-60 שניות לכל היותר. זה יתועד.
- **Scale-out:** עם `IMemoryCache` כל מופע שרת מחזיק Cache משלו, ו-Invalidation במופע אחד לא מגיע לאחרים. הפתרון: `IDistributedCache` (Redis) עם אותו Key ואותה Invalidation, או הודעות Invalidation ב-Redis Pub/Sub (או `HybridCache` עם Tags ב-.NET 9+). זה יתועד ולא ימומש.

### 3.5 אסטרטגיית בדיקות
- **Unit:** לוגיקה טהורה, כלומר `StatusTransitionPolicy` ו-Validation של פרמטרי שאילתה. מהיר, בלי DB.
- **Integration:** `WebApplicationFactory` מול **SQL Server אמיתי** (DB נפרד `RequestsManagement_Tests`). Concurrency מבוסס `rowversion` ותרגום LINQ ל-SQL הם מה שצריך לבדוק, ו-EF InMemory או SQLite לא מדמים אותם נאמנה. הנתונים מאופסים לפני כל בדיקה, עם Seed קטן ודטרמיניסטי.
- **בלי Testcontainers**, כי הוא דורש Docker. החיסרון: מי שמריץ את הבדיקות צריך SQL Server מקומי (Connection String ניתן להגדרה). זה יתועד.
- **Angular:** בלי בדיקות אוטומטיות (לא נדרש). זה יופיע כמגבלה ידועה.

### 3.6 ביצועים ואינדקסים
- `Status` ו-`Priority` נשמרים כ-`tinyint`, כדי שהאינדקסים יהיו צרים. ב-JSON הם מוצגים כמחרוזות.
- `Id` הוא `int IDENTITY` ו-Clustered (צר ורציף).
- **אינדקסים ראשוניים** (יאומתו במדידה ב-T-801, ויכול להיות שחלק יוסר):

| אינדקס | תרחיש |
|---|---|
| `IX_Requests_CreatedAt` (CreatedAt DESC, Id) | מיון ברירת מחדל, טווח תאריכים, דפדוף |
| `IX_Requests_Status_CreatedAt` (Status, CreatedAt DESC) | סינון לפי סטטוס + מיון ברירת מחדל; GROUP BY Status |
| `IX_Requests_Priority_CreatedAt` (Priority, CreatedAt DESC) | סינון לפי עדיפות; GROUP BY Priority |
| `IX_Requests_AssignedTo_CreatedAt` (AssignedTo, CreatedAt DESC) | "הפניות שלי" |
| `IX_Requests_OrganizationName` (OrganizationName) | סינון StartsWith ומיון לפי ארגון |
| `IX_RequestStatusHistory_RequestId_ChangedAt` | שליפת היסטוריה |

- **Bottlenecks צפויים (ייבדקו ולא יונחו מראש):**
  1. חיפוש `Contains` מתורגם ל-`LIKE '%x%'`, שאינו Sargable ולכן גורם ל-Scan. השיפור האפשרי: Full-Text Index.
  2. `COUNT(*)` ו-`OFFSET` בדפים עמוקים. השיפור האפשרי: Keyset Pagination.
  3. כל אינדקס נוסף מייקר כתיבה. לכן אין אינדקס לכל קומבינציה.
- **מניעת טעינה מלאה:** כל השאילתה (Where, OrderBy, Skip/Take, Select ל-DTO) נבנית על `IQueryable` ומבוצעת ב-DB. `AsNoTracking` בקריאות. אין `ToList()` לפני הסינון.

---

## 4. טבלת משימות (סיכום)

| ID | משימה | Epic | הערכה (ש') | עדיפות | תלויות | יום | סטטוס |
|---|---|---|---|---|---|---|---|
| T-000 | תכנון, ניתוח דרישות ו-TASKS.md | Planning | 0.5 | P1 | — | 0 (22.10) | Done  |
| T-101 | Solution skeleton, Git repo, DI בסיסי | E1 Setup & Data | 0.5 | P1 | T-000 | 1 | Done  |
| T-102 | Domain model, DbContext, Migration ראשונה | E1 | 0.5 | P1 | T-101 | 1 | Done  |
| T-103 | תכנון והגדרת אינדקסים | E1 | 0.25 | P1 | T-102 | 1 | Done  |
| T-104 | Seeder ל-100,000 רשומות שאפשר להריץ מחדש | E1 | 0.5 | P1 | T-102 | 1 | Done  |
| T-304 | Global error handling, ProblemDetails, Logging | E3 | 0.25 | P1 | T-101 | 1 | Done  |
| T-201 | Query DTOs, פרמטרים ו-Validation | E2 Query API | 0.5 | P1 | T-102 | 2 | Done  |
| T-202 | RequestQueryService: filtering, search, sort | E2 | 0.75 | P1 | T-201 | 2 | Done |
| T-203 | RequestsController: GET list | E2 | 0.25 | P1 | T-202 | 2 | Done |
| T-204 | Aggregations endpoint | E2 | 0.25 | P1 | T-202 | 2 | Done |
| T-301 | StatusTransitionPolicy | E3 Update & Audit | 0.25 | P1 | T-102 | 2 | Done  |
| T-302 | עדכון סטטוס בודד: Concurrency ו-Audit | E3 | 0.75 | P1 | T-301, T-304 | 3 | Done  |
| T-303 | History endpoint | E3 | 0.25 | P1 | T-302 | 3 | Done  |
| T-401 | Bulk status update (Partial Success) | E4 Bulk | 0.75 | P1 | T-302 | 3 | Done  |
| T-501 | Cache ל-Summary: Expiration ו-Invalidation | E5 Cache | 0.5 | P1 | T-204, T-302, T-401 | 3 | Done |
| T-601 | תשתית Integration Tests | E6 Tests | 0.5 | P1 | T-203 | 4 | Done |
| T-602 | Unit tests: מעברי סטטוס ו-Validation | E6 | 0.25 | P1 | T-201, T-301 | 4 | Done |
| T-603 | Integration: שליפה, סינון ודפדוף | E6 | 0.25 | P1 | T-601 | 4 | Done |
| T-604 | Integration: Concurrency Conflict ו-Audit | E6 | 0.5 | P1 | T-601, T-302 | 4 | Done |
| T-801 | מדידת ביצועים ו-docs/performance.md | E8 Perf | 0.75 | P1 | T-104, T-203, T-204 | 4 | Done |
| T-701 | Angular scaffold, Proxy, Models, ApiService | E7 Angular | 0.5 | P1 | T-203 | 4 | Done |
| T-702 | RequestsPageComponent: filters, search, sort, debounce | E7 | 1.5 | P1 | T-701 | 4 | Done |
| T-703 | Summary panel | E7 | 0.25 | P1 | T-701, T-204 | 5 | Done |
| T-704 | עדכון סטטוס וטיפול ב-409 ב-UI | E7 | 0.5 | P1 | T-702, T-302 | 6 | Done |
| T-705 | תצוגת היסטוריה | E7 | 0.5 | P1 | T-702, T-303 | 6 | Done |
| T-901 | README: הרצה, גרסאות, מבנה | E9 Docs | 0.5 | P1 | כל ה-P1 | 6 | Done |
| T-902 | docs/decisions.md (כולל AI usage) | E9 | 0.5 | P1 | T-401, T-501, T-801 | 6 | To Do |
| T-903 | Run-through מ-Clone נקי, Push והגשה | E9 | 0.5 | P1 | כל השאר | 6 | To Do |
| T-605 | Integration: 404, 400 ו-Bulk חלקי | E6 | 0.25 | P2 | T-601, T-401 | Reserve | To Do |
| T-606 | Test ל-Cache Invalidation | E6 | 0.25 | P3 | T-501 | Reserve | To Do |
| T-706 | Bulk ב-UI (Checkboxes + עדכון מרובה) | E7 | 0.75 | P3 | T-704, T-401 | Reserve | To Do |

**סיכומי זמן**

| קבוצה | שעות |
|---|---|
| P1 (ימים 1–6, בלי T-000) | **13.25** |
| P2 | 0.25 |
| P3 | 1.0 |
| **סה"כ כל המשימות** | **14.5** |
| תקציב | 12 (בסיס) עד 14 (מורחב) |

> **הערכה כנה:** ליבת החובה (P1) מוערכת ב-13.25 שעות, יותר מ-12 שעות הבסיס. מעשית, צריך כ-13–14 שעות (בממוצע כ-2:15 ביום), או להפעיל את רשימת הקיצוצים ב-§7.3. אם נעבוד 12 שעות בדיוק בלי קיצוצים, לא יישאר זמן Buffer.

---

## 5. פירוט משימות

### T-000 — תכנון ו-TASKS.md · 0.5ש' · P1 · In Progress
- **תיאור:** קריאת ה-PDF, ניתוח דרישות, ארכיטקטורה, החלטות, פירוק למשימות ולוח זמנים.
- **תוצר:** `TASKS.md` (המסמך הזה).
- **AC:** לכל דרישה ב-PDF יש מזהה ומשימה; לכל משימה יש תיאור, תוצר, AC, תלויות, הערכה, עדיפות וסטטוס; האישור שלך התקבל.

### E1 — Setup & Data

#### T-101 — Solution skeleton · 0.5ש' · P1
- **תיאור:** `git init`, `.gitignore` (dotnet + node), `backend/RequestsManagement.sln`, פרויקט `RequestsManagement.Api` (net8.0) ופרויקט `RequestsManagement.Tests` (xUnit). חבילות: `Microsoft.EntityFrameworkCore.SqlServer`, `.Design`, `Microsoft.AspNetCore.Mvc.Testing`. Swagger, `JsonStringEnumConverter`, Connection String ב-`appsettings.Development.json`. **בדיקת `node -v`** (Angular 20 דורש Node ‎20.19 ומעלה).
- **תוצר:** Solution שמתקמפל, ו-`/swagger` עולה.
- **AC:** `dotnet build` ו-`dotnet test` עוברים (גם בלי בדיקות); יש Commit ראשון; גרסת Node תואמת או שהבעיה תועדה.

#### T-102 — Domain model, DbContext, Migration · 0.5ש' · P1
- **תיאור:** `Request` (כל השדות מ-R-0.1), `RequestStatusHistory` (Id, RequestId FK, PreviousStatus, NewStatus, ChangedAt, ChangedBy), enums `RequestStatus` ו-`RequestPriority` (כ-tinyint). Fluent Configuration: אורכי מחרוזות, `RowVersion.IsRowVersion()`, Required. Migration `InitialCreate`.
- **תוצר:** DB `RequestsManagement` נוצר עם `dotnet ef database update`.
- **AC:** הטבלאות קיימות עם הטיפוסים הנכונים; `RowVersion` מסוג `rowversion`; יש FK מ-History ל-Requests.

#### T-103 — Index design · 0.25ש' · P1
- **תיאור:** הגדרת האינדקסים מ-§3.6 ב-Configuration וב-Migration, עם הערה קצרה בקוד על תכלית כל אינדקס.
- **תוצר:** Migration `AddIndexes` (נפרדת, כדי שאפשר יהיה למדוד לפני ואחרי ב-T-801).
- **AC:** האינדקסים קיימים ב-DB; לכל אחד יש תרחיש מוצדק ברשימה.

#### T-104 — Seeder 100K · 0.5ש' · P1
- **תיאור:** `DataSeeder` שמופעל ב-`dotnet run -- --seed [count=100000]`. מוחק נתונים קיימים ומכניס פניות עם `Random(seed)` קבוע (דטרמיניסטי): ארגונים ומטפלים מרשימה סגורה, התפלגות סטטוסים ועדיפויות, ו-CreatedAt פרוש על שנתיים. הכנסה ב-Batches (`AddRange` של 5,000 + `ChangeTracker.Clear()`). אם זה איטי מדי, מעבר ל-`SqlBulkCopy`.
- **תוצר:** 100,000 רשומות; פקודה מתועדת.
- **AC:** הרצה חוזרת מייצרת שוב בדיוק 100,000 רשומות; זמן ההרצה נמדד ונרשם (מדידה אמיתית בלבד).

### E2 — Query API

#### T-201 — Query DTOs ו-Validation · 0.5ש' · P1
- **תיאור:** `RequestQueryParameters` (page, pageSize, status[], priority[], organizationName, assignedTo, createdFrom, createdTo, search, sortBy, sortDirection), `PagedResult<T>`, `RequestListItemDto`, `RequestDetailsDto`, `RequestSummaryDto`. Validation עם DataAnnotations ו-`IValidatableObject`: `pageSize` בין 1 ל-100, `page` ≥ 1, `sortBy` מתוך Whitelist (`createdAt, updatedAt, priority, status, title, organizationName`), `createdFrom ≤ createdTo`, אורך מקסימלי ל-search.
- **תוצר:** DTOs ו-Validation; 400 בפורמט `ValidationProblemDetails`.
- **AC:** פרמטר לא תקין מחזיר 400 עם שם השדה והודעה ברורה.

#### T-202 — RequestQueryService · 0.75ש' · P1
- **תיאור:** בניית `IQueryable` עם `AsNoTracking`: Where לכל פילטר שהתקבל, `search` ב-Contains על Title או OrganizationName, OrderBy דינמי מתוך Whitelist (Switch, בלי Reflection) עם `ThenBy(Id)` לדפדוף יציב, `CountAsync` ואז `Skip/Take` ו-`Select` ל-DTO. CancellationToken מועבר בכל קריאה.
- **תוצר:** Service עם Interface, רשום ב-DI כ-Scoped.
- **AC:** ה-SQL שנוצר (מ-Log) כולל `WHERE`, `ORDER BY` ו-`OFFSET/FETCH`; אין חומרה (Materialization) לפני הסינון.

#### T-203 — Controller GET · 0.25ש' · P1
- **תיאור:** `GET /api/requests` ו-`GET /api/requests/{id}` (404 אם לא קיים).
- **AC:** בדיקה ידנית ב-Swagger של שילוב פילטרים ומיון Asc/Desc.

#### T-204 — Aggregations endpoint · 0.25ש' · P1 ✓ Done
- **תיאור:** `GET /api/requests/summary`: `GroupBy(Status).Count` ו-`GroupBy(Priority).Count` ב-DB, וסה"כ.
- **AC:** הסכום לפי Status שווה לסה"כ; שתי שאילתות GROUP BY ב-SQL ולא חישוב בזיכרון.
- **אימות (24.10):** HTTP 200 — ByStatus: New 25019, InProgress 25153, Waiting 24886, Completed 24942 (סה"כ 100,000); ByPriority: Low 33642, Medium 33074, High 33284 (סה"כ 100,000). שתי שאילתות GROUP BY בלבד — אפס רשומות נטענו לזיכרון.

### E3 — Status Update, Concurrency, Audit

#### T-301 — StatusTransitionPolicy · 0.25ש' · P1
- **תיאור:** מחלקה סטטית עם טבלת המעברים המותרים מ-§1.13 ופונקציה `CanTransition(from, to)`.
- **AC:** מכוסה ב-Unit Tests (T-602).
- **אימות (24.10):** קובץ `Domain/StatusTransitionPolicy.cs` נוצר עם `switch` exhaustive על `from`. מעברים מותרים: `New→InProgress/Waiting`, `InProgress→Waiting/Completed`, `Waiting→InProgress/Completed`. `Completed` טרמינלי. מעבר לאותו סטטוס אסור. נבדק כחלק מאימות T-302.

#### T-302 — עדכון סטטוס בודד · 0.75ש' · P1
- **תיאור:** `PATCH /api/requests/{id}/status`. Service: טעינה → 404 אם לא קיים → בדיקת מעבר (400) → `Entry.Property(RowVersion).OriginalValue = dto.RowVersion` → עדכון Status ו-`UpdatedAt = TimeProvider.GetUtcNow()` → הוספת `RequestStatusHistory` → `SaveChangesAsync` אחד → במקרה של `DbUpdateConcurrencyException` מוחזר 409 עם הסטטוס וה-rowVersion הנוכחיים. Log ברמת Information על הצלחה ו-Warning על Conflict, בלי Payload מלא.
- **תוצר:** Endpoint ו-Service.
- **AC:** תשובת 200 מכילה rowVersion חדש ו-UpdatedAt מעודכן; עדכון עם rowVersion ישן מחזיר 409 ולא משנה כלום ב-DB; כל הצלחה יוצרת בדיוק שורת Audit אחת, באותה טרנזקציה.
- **אימות (24.10):** RequestId 93710. (1) `New→InProgress` עם rowVersion תקין: HTTP 200, rowVersion השתנה, updatedAt עודכן. (2) `InProgress→New` (אסור): HTTP 400, `"detail": "Transition from 'InProgress' to 'New' is not allowed."`. (3) `InProgress→Completed` עם rowVersion ישן: HTTP 409, `currentStatus=InProgress`, `currentRowVersion` תקין. (4) Id לא קיים (999999999): HTTP 404. אימות DB: `SELECT * FROM RequestStatusHistory WHERE RequestId=93710` — שורה אחת בדיוק, `PreviousStatus=0`, `NewStatus=1`, `ChangedBy=tester-01`, timestamp תואם. ללא רשומה אחרי 409 (rollback אוטומטי).

#### T-303 — History endpoint · 0.25ש' · P1
- **תיאור:** `GET /api/requests/{id}/history`, ממוין ChangedAt DESC; 404 אם הפנייה לא קיימת.
- **AC:** מחזיר DTO עם 5 השדות הנדרשים (R-3.1).
- **אימות (24.10):** (1) `GET /api/requests/93710/history` — HTTP 200, מערך עם שורה אחת: `New→InProgress`, `changedBy=tester-01`. (2) `GET /api/requests/999999999/history` — HTTP 404. (3) `GET` לפנייה קיימת ללא היסטוריה — HTTP 200 עם מערך ריק `[]`. האינדקס `IX_RequestStatusHistory_RequestId_ChangedAt` מכסה את השאילתה (Seek על RequestId, ממוין ChangedAt DESC).

#### T-304 — Global error handling ו-Logging · 0.25ש' · P1
- **תיאור:** `AddProblemDetails` ו-`IExceptionHandler` שמחזיר 500 בפורמט ProblemDetails עם traceId ובלי Stack Trace מחוץ ל-Development. `OperationCanceledException` נרשם ב-Log ברמה נמוכה ולא כשגיאה.
- **AC:** Exception יזום מחזיר ProblemDetails; ה-Log לא חושף Connection String או נתונים רגישים.

### E4 — Bulk

#### T-401 — Bulk status update · 0.75ש' · P1 ✓ Done
- **תיאור:** `POST /api/requests/status/bulk` לפי §3.3. Validation ברמת הבקשה (400), טעינה אחת של כל הפריטים, ולכל פריט: NotFound, InvalidTransition, ניסיון שמירה, Conflict או Updated. Log של סיכום (מספר שהצליחו ומספר שנכשלו).
- **AC:** שליחה של 101 פריטים מחזירה 400; תמהיל של פריט קיים, פריט לא קיים ו-rowVersion ישן מחזיר 200 עם Outcome נכון לכל פריט; Audit נוצר רק לפריטים שהצליחו.
- **אימות (24.10):** (1) `id=0` (לא חוקי): HTTP 400. (2) `rowVersion` לא תקין Base64: HTTP 400. (3) Base64 תקין אך לא 8 בתים: HTTP 400. (4) שני פריטים תקינים (`New→InProgress`): HTTP 200, שניהם `Updated`, rowVersion חדש לכל אחד. (5) Audit נוצר בדיוק לפריטים שהצליחו — אומת ב-DB. (6) Partial Success עם `NotFound`: HTTP 200, `outcome: "NotFound"` לפריט שלא קיים, `outcome: "Updated"` לפריט הקיים. (7) Partial Success עם `InvalidTransition`: HTTP 200, `outcome: "InvalidTransition"` לפריט בסטטוס טרמינלי. (8) `rowVersion` ישן: HTTP 200, `outcome: "Conflict"` עם `rowVersion` עדכני של השרת. (9) פריט תקין אחרי Conflict: עודכן בהצלחה — מוכיח שה-ChangeTracker נוקה נכון אחרי הכשל. (10) `duplicateIds`: HTTP 400. — תרחישים שלא נבדקו ידנית: items ריק, מעל 100 פריטים, תמהיל malformed + תקין — יכוסו ב-T-605.

### E5 — Cache

#### T-501 — Summary cache · 0.5ש' · P1 ✓ Done
- **תיאור:** `RequestSummaryService` עם `IMemoryCache.GetOrCreateAsync` ו-Absolute Expiration שנקבע ב-Configuration (ברירת מחדל 60 שניות). מתודת `Invalidate()` נקראת מ-`RequestStatusService` אחרי Commit מוצלח.
- **AC:** קריאה שנייה לא מגיעה ל-DB (נראה ב-Log); אחרי עדכון סטטוס ה-Summary משקף את השינוי מיד.
- **מימוש:** `IMemoryCache` רשום ב-DI; `SummaryCacheOptions` נקרא מ-`appsettings.json` (ברירת מחדל 60 שניות). `RequestQueryService.GetSummaryAsync` משתמש ב-`GetOrCreateAsync` עם Absolute Expiration. `RequestStatusService` קורא ל-`cache.Remove` אחרי כל `SaveChangesAsync` מוצלח — גם בעדכון יחיד וגם בתוך לולאת Bulk. רמות Debug-Log נוספו ל-`RequestQueryService` ו-`RequestStatusService` ב-`appsettings.Development.json`.
- **אימות (25.10):** (1) בקשה ראשונה ל-Summary: Cache MISS + שתי שאילתות GROUP BY ב-Log. (2) בקשה שנייה מיד אחרי: Cache HIT, ללא שאילתות SQL. (3) עדכון סטטוס יחיד מוצלח: INVALIDATED ב-Log. (4) Bulk עם 2 פריטים מוצלחים: INVALIDATED פעמיים ב-Log (אחד לכל פריט). (5) Bulk כושל (InvalidTransition): ללא INVALIDATED, בקשת Summary לאחר מכן מחזירה Cache HIT — ה-Cache לא נמחק.

### E6 — Tests

#### T-601 — Integration test infra · 0.5ש' · P1
- **תיאור:** `ApiFactory : WebApplicationFactory<Program>` שמחליף Connection String ל-DB של בדיקות, מריץ `Migrate()` פעם אחת, ומבצע Reset ו-Seed קטן לפני כל בדיקה. `[Collection]` כדי שבדיקות שחולקות DB ירוצו בסדרה.
- **AC:** `dotnet test` רץ מקומית מול SQL Server; Connection String ניתן לשינוי דרך משתנה סביבה.

#### T-602 — Unit tests · 0.25ש' · P1
- **תיאור:** `[Theory]` למעברים מותרים ואסורים; Validation של `RequestQueryParameters` (pageSize 101, sortBy לא חוקי, from > to).
- **AC:** לפחות 2 בדיקות משמעותיות שעוברות.

#### T-603 — Integration: שליפה וסינון · 0.25ש' · P1
- **תיאור:** GET עם פילטר Status, מיון ו-pageSize על Seed ידוע.
- **AC:** בודק totalCount, גודל הדף, סדר המיון ותוכן הפילטר.

#### T-604 — Integration: Concurrency · 0.5ש' · P1
- **תיאור:** (א) שני PATCH עם אותו rowVersion: הראשון מקבל 200 והשני 409. (ב) הרצה מקבילית עם `Task.WhenAll`: בדיוק אחד מצליח. (ג) נבדק שנוצרה שורת Audit אחת בלבד.
- **AC:** הבדיקות עוברות באופן יציב (5 הרצות ברצף בלי Flakiness).

#### T-605 — Integration: 404, 400 ו-Bulk · 0.25ש' · P2
- **AC:** PATCH ל-Id לא קיים מחזיר 404; מעבר אסור מחזיר 400; Bulk מעורב מחזיר Outcomes נכונים.

#### T-606 — Cache invalidation test · 0.25ש' · P3
- **AC:** GET summary, אחר כך PATCH, ואחר כך GET summary מחזיר ספירות מעודכנות.

### E7 — Angular

#### T-701 — Scaffold ו-ApiService · 0.5ש' · P1
- **תיאור:** `ng new` (Standalone, בלי SSR), `proxy.conf.json` ל-API (במקום CORS), Interfaces ל-DTOs, `RequestsApiService` עם `getRequests(params)`, `getSummary()`, `updateStatus()`, `getHistory()`. כל המתודות מחזירות Observables.
- **AC:** `ng serve` עולה, וקריאה ל-API עוברת דרך ה-Proxy.

#### T-702 — עמוד הרשימה · 1.5ש' · P1
- **תיאור:** `RequestsPageComponent` מחזיק `BehaviorSubject` של מצב השאילתה. החיפוש עובר `debounceTime(300)` (no distinctUntilChanged -- removed; see bug fix log in section 6). מצב השאילתה (פילטרים, מיון, דף) נכנס ל-`switchMap(api.getRequests)` שמבטל בקשה קודמת. `catchError` מחזיר מצב Error בלי להפיל את ה-Stream. מצבי `loading | loaded | empty | error` מוצגים, עם כפתור "נסה שוב". שינוי פילטר מאפס את הדף ל-1. `RequestsFiltersComponent` ו-`RequestsTableComponent` הם רכיבי תצוגה (Inputs/Outputs), עם כותרות שמיונות ב-Click ודפדוף. עיצוב: CSS בסיסי, בלי ספריית UI.
- **AC:** ב-DevTools רואים שהקלדה מהירה שולחת בקשה אחת, ובקשות ישנות מבוטלות (Canceled); כל מיון, סינון ודפדוף שולח פרמטרים לשרת; שלושת המצבים מוצגים כנדרש.

#### T-703 — Summary panel · 0.25ש' · P1
- **AC:** מציג ספירה לפי Status ולפי Priority, ומתרענן אחרי עדכון סטטוס.

#### T-704 — עדכון סטטוס ו-409 · 0.5ש' · P1
- **תיאור:** Select של סטטוס בשורה, ושליחה עם rowVersion. בהצלחה: עדכון השורה ורענון ה-Summary. ב-409: הודעה "הפנייה עודכנה על ידי משתמש אחר", רענון השורה מהשרת ואפשרות לנסות שוב. ב-400: הצגת הודעת השרת.
- **AC:** תרחיש ידני: שני טאבים מעדכנים את אותה פנייה, והשני מקבל הודעת Conflict ונתונים מעודכנים.

#### T-705 — תצוגת היסטוריה · 0.5ש' · P1 · Done
- **תיאור:** `RequestHistoryComponent` נפתח בלחיצה על שורה (פאנל צד או שורה נפתחת), עם Loading ו-Empty משלו.
- **AC:** מציג Previous → New, ChangedAt ו-ChangedBy.
- **מימוש:** כפתור History בכל שורה בטבלה פותח שורת פירוט מתחתיה עם `GET /api/requests/{id}/history`. History נטען on-demand ומגובה-Cache לכל דף. אחרי עדכון סטטוס מוצלח, ה-Cache מתבטל ו-History נטען מחדש אם הפאנל פתוח. כולל loading/empty/error states.
- **נבדק ידנית:** HTTP 200, רשומות מוצגות נכון, Cache invalidation עובד בלי Refresh, Build ייצור עובר ללא שגיאות.

#### T-706 — Bulk UI · 0.75ש' · P3
- **AC:** בחירת עד 100 שורות, עדכון, והצגת סיכום הצלחות וכישלונות לפי פריט.

### E8 — Performance

#### T-801 — מדידת ביצועים · 0.75ש' · P1
- **תיאור:** שתי פעולות: (1) `GET /api/requests` עם Status, טווח תאריכים ומיון CreatedAt DESC, בדף ראשון ובדף עמוק; (2) `GET /api/requests/summary`. בנוסף, אם יש זמן, חיפוש טקסט כדי להדגים את ה-Bottleneck. שיטה: לכידת ה-SQL מה-Log של EF, הרצה ב-SSMS עם Actual Execution Plan ו-`SET STATISTICS IO, TIME ON`, לפני ואחרי האינדקסים (`ALTER INDEX ... DISABLE/REBUILD`).
- **תוצר:** `docs/performance.md` עם ה-SQL, ה-Plan (צילום מסך או `.sqlplan`), Logical Reads וזמן CPU, האינדקסים והסיבה, Bottleneck והצעת שיפור.
- **AC:** כל המספרים במסמך נמדדו בפועל; אין מספרים משוערים שמוצגים כאילו נמדדו.

### E9 — Docs & Submission

#### T-901 — README · 0.5ש' · P1
- **תוכן:** דרישות מקדימות וגרסאות, Connection String, `dotnet ef database update`, הרצת ה-Seeder, הרצת ה-API וה-UI, הרצת הבדיקות, מבנה הפתרון, הנחות (§1.13), וקישורים ל-docs ול-TASKS.md.
- **AC:** מישהו אחר יכול להריץ את הפתרון מאפס רק לפי ה-README.

#### T-902 — docs/decisions.md · 0.5ש' · P1
- **תוכן:** DB ואינדקסים, Concurrency, Bulk (כולל נימוק Partial Success), Cache (Invalidation ו-Scale-out), שתי החלטות עם חלופות, מגבלה ידועה ושיפור אפשרי, ופירוט השימוש ב-AI.
- **שיטת עבודה:** בסוף כל יום נרשמות 5–10 דקות של הערות (כולל מה נבדק ושונה ידנית בקוד שה-AI הציע). הזמן הזה כלול בהערכות היומיות, כך ש-T-902 הוא בעיקר עריכה.
- **AC:** כל סעיפי R-11.x מכוסים.

#### T-903 — Run-through והגשה · 0.5ש' · P1
- **תיאור:** Clone נקי לתיקייה חדשה והרצה לפי ה-README, `dotnet test`, עדכון סטטוסים ב-TASKS.md, Push, ובדיקת הגישה ל-Repo (Public, או הזמנת הבודקים).
- **AC:** הכול רץ מה-Clone הנקי; ה-Repo נגיש; לא נשלח ZIP.

---

## 6. תוכנית עבודה ל-6 ימים (מעודכן 22.10)

**כלל בסיסי: מקסימום 2 שעות ביום. יום ההגשה (28.10) — אימות והגשה בלבד.**
**הערכות הזמן הן יעדים, לא התחייבות. בסוף כל יום: זמן בפועל + מה לא הושלם.**

| יום | תאריך | מקסימום | נושא | משימות | הערכה |
|---|---|---|---|---|---|
| 0 | 22.10 | -- | **DONE on day 0** -- T-000..T-702 + 3 bug fixes; manual verification passed | T-000, T-101..T-104, T-304, T-201..T-203, T-301, T-701, T-702 | -- (AI-assisted) |
| 1 | 23.10 | 2:00 | Aggregations + Status Update + Audit | T-204, T-302, T-303 | 1.25 |
| 2 | 24.10 | 2:00 | Bulk + Cache + Summary panel (Angular) | T-401, T-501, T-703 | 1.5 |
| 3 | 25.10 | 2:00 | Tests: infra + unit + integration (filtering) | T-601, T-602, T-603 | 1.0 |
| 4 | 26.10 | 2:00 | Tests: Concurrency + Performance measurement | T-604, T-801 | 1.25 |
| 5 | 27.10 | 2:00 | Angular part 2 + README | T-704, T-705, T-901 | 1.5 |
| 6 | 28.10 | 1:30 | decisions.md + Run-through + Push | T-902, T-903 | 1.0 |
| Reserve | -- | -- | | T-605, T-606, T-706 | 0.75 |

**שיפורים לעומת הגרסה הקודמת**
- **Angular מתחיל ביום 4** (ברגע שיש GET) במקום ביום 5.
- **יום 28.10 מוגן:** ה-Run-through וההגשה מתוכננים, ויום ההגשה לא מכיל פיתוח כבד.
- **כל יום מוגבל ב-2:00** — אם נגמרות השעות, מה שלא הושלם עובר ליום הבא עם תיעוד.

**עקרונות לסדר הביצוע**
- **ה-Backend קודם:** הוא מהווה כ-60% מהניקוד, ו-Angular תלוי בו.
- **Angular** מתחיל ברגע ש-GET עובד (לא מחכים לסוף הכל).
- **Commit בסוף כל משימה:** הודעה עם מזהה המשימה (`T-302: ...`).

**נקודות ביקורת (Checkpoints)**
- **סוף יום 2:** GET עם פילטרים עובד על 100K רשומות. אם לא — קיצוצים לפי §7.3.
- **סוף יום 4:** Status Update + Bulk עובדים. Angular Scaffold מוכן.
- **סוף יום 5:** `dotnet test` ירוק, Angular מציג רשימה עם פילטרים.
- **28.10 בבוקר:** אם ה-Backend מלא — מתמקדים ב-UI ובתיעוד. אם לא — מוותרים על P3.

---

---

## 6.1 Work Log -- 22.10.2026 (actual)

All work below was completed on Oct 22 (day 0), ahead of the original schedule.
Implementation was AI-assisted (Claude Sonnet 4.6); every diff was reviewed manually
before commit.

### Commits (verified from git log)

| Hash | Message |
|---|---|
| 7c8a9e7 | T-101, T-102: Solution scaffold + Domain model + EF Core setup |
| f034f1e | T-103, T-304: Database indexes + Global error handling |
| 4251046 | T-104: Deterministic 100K seeder -- bug fix and verified |
| e6ebb38 | T-201/T-202/T-203 (partial): GET /api/requests with pagination |
| 2d02ba3 | feat(query): filtering, search, sorting -- Milestone 3B |
| b23fcc8 | feat(angular): Angular 20 requests list UI -- Milestone 4A |
| d189d4a | feat(angular): filter, search, sort controls -- Milestone 4B |
| 0052ab9 | fix(angular): duplicate async-pipe + void distinctUntilChanged |

### Three defects found and fixed

| # | Defect | Root cause | Fix |
|---|---|---|---|
| BF-1 | `Subject<void>` + `distinctUntilChanged` suppressed all search emissions after the first | `distinctUntilChanged` compares `undefined === undefined` -- always true, so every emission after the first is swallowed | Removed `distinctUntilChanged` from the text-change pipe (commit 0052ab9) |
| BF-2 | Two `state$ \| async` subscriptions in HTML caused two HTTP calls per filter change | Total-count badge and main content each had their own `@if (state$ \| async; as state)` block | Wrapped entire template in a single outer `@if`; inner state references use the captured `state` variable (commit 0052ab9) |
| BF-3 | `createdTo=2024-06-30` excluded all records created after midnight on June 30 | Angular `type="date"` sends `YYYY-MM-DD`; ASP.NET model-binds it as midnight; `<= midnight` excludes the rest of the day | Changed to exclusive upper bound: `exclusiveUpperBound = createdTo.Date.AddDays(1)`; filter becomes `CreatedAt < exclusiveUpperBound` (sargable, no function on column) |

### Manual verification performed (22.10)

- Repeated free-text searches with debounce: single HTTP request per settled input confirmed in DevTools Network tab.
- Combined filters (Status + Priority + date range + search): results matched expected subset.
- Sort by Created date / Priority / Title, Asc and Desc: correct ordering confirmed.
- Pagination: page navigation resets on filter change; current page preserved on sort-only change.
- Reset filters: all controls clear, full result set restored, single HTTP call.
- One HTTP request per filter change: stale requests cancelled (status "Canceled" in Network tab).
- Inclusive `createdTo` fix: set `createdTo = 2024-06-30`; records created on June 30 at times > 00:00 now appear; July 1 records absent.

### Non-coding work

- Code review of every generated diff before commit.
- Debugging duplicate subscription bug: traced via DevTools and RxJS pipe analysis.
- Debugging `distinctUntilChanged` suppression: identified via manual test (search worked once then stopped).
- AI-assisted implementation throughout; all prompts written and reviewed by developer.

---

## 7. סיכונים, דרישות בסיכון וקיצוצים

### 7.1 סיכונים

| # | סיכון | השפעה | הסתברות | מיטיגציה |
|---|---|---|---|---|
| K-1 | Angular לוקח יותר מ-3.25 שעות (15 נק') | גבוהה | גבוהה | בלי ספריית UI, עמוד אחד, Checkpoint בסוף יום 5, רשימת קיצוצים |
| K-2 | יום 6 (מועד ההגשה) עמוס (2.5 שעות) | גבוהה | בינונית | תיעוד שוטף, README כבר מיום 4, אם אפשר להקדים את T-701 לסוף יום 4 |
| K-3 | גרסת Node 20 נמוכה מ-20.19, ואז Angular 20 לא יותקן | בינונית | בינונית | בדיקה ב-T-101 ועדכון ל-Node 20 LTS האחרון או 22 |
| K-4 | Integration Tests תלויים ב-SQL Server מקומי אצל הבודק | בינונית | בינונית | Connection String ניתן להגדרה, תיעוד ב-README, LocalDB כברירת מחדל |
| K-5 | Seed של 100K דרך EF איטי | נמוכה | בינונית | Batches ו-`ChangeTracker.Clear`; Fallback ל-`SqlBulkCopy` |
| K-6 | Flakiness בבדיקת מקביליות | בינונית | נמוכה | הבדיקה הדטרמיניסטית (rowVersion ישן) היא העיקרית; הבדיקה המקבילית היא תוספת |
| K-7 | אין SSMS או Azure Data Studio למדידת Plan | בינונית | נמוכה | `SET STATISTICS IO/TIME` ו-`SET SHOWPLAN_XML` דרך `sqlcmd` |
| K-8 | קוד AI שלא נבדק לעומק | גבוהה | בינונית | Review של כל Diff לפני Commit, ורישום מה שונה ידנית (R-11.9) |

### 7.2 דרישות שעלולות לא להיכנס במלואן
כל הדרישות ב-PDF הן חובה. המטרה היא לכסות את כולן לפחות ברמה בסיסית. הנקודות שבהן ההיקף עלול להצטמצם:
- **R-6.x ביצועים:** אם יחסר זמן, נמדוד את שתי הפעולות במצב אחרי האינדקסים בלבד, בלי השוואה לפני ואחרי.
- **R-5.x Angular:** עיצוב מינימלי, וההיסטוריה תוצג בשורה נפתחת ולא בדיאלוג.
- **R-8.x בדיקות:** ייתכן שיהיו רק 4–5 בדיקות (המינימום הנדרש הוא 4) בלי T-605 ו-T-606.
- **Bulk ב-UI (T-706):** לא נדרש ב-PDF ב-Angular, ולכן הוא P3.

### 7.3 רשימת קיצוצים (לפי הסדר, אם מפגרים בלוח הזמנים)
1. ויתור על P3: T-706 ו-T-606 (חיסכון של 1 שעה).
2. ויתור על T-605 (חיסכון של 0.25 שעות). 4 הבדיקות הנדרשות עדיין מכוסות ב-T-602 עד T-604.
3. T-801 במצב אחרי האינדקסים בלבד (חיסכון של כ-0.25 שעות).
4. T-705 כשורה נפתחת פשוטה (חיסכון של כ-0.25 שעות).
5. T-704: ב-409 רק הודעה ורענון השורה, בלי מסך השוואה.

**לא נחתך בשום מצב:** סינון, מיון ודפדוף בצד השרת; Concurrency שנאכף ב-DB; Audit; Cache עם Invalidation; Integration Test; README והחלטות.

---

## 8. מיפוי לניקוד (לתעדוף)

| תחום (משקל) | Epics עיקריים | שעות מתוכננות |
|---|---|---|
| Backend .NET (17) | E1, E2, E3 (T-304) | ~3.5 |
| REST API (13) | E2, E4 | ~2.5 |
| Angular (15) | E7 | 3.25 (+0.75 P3) |
| DB וביצועים (13) | T-102–T-104, T-204, T-801 | ~2.0 |
| Concurrency + Audit (11) | T-302, T-303, T-401, T-604 | ~2.25 |
| תכנון עבודה (8) | T-000 (+ עדכון סטטוסים) | ~0.5 |
| איכות קוד (9) | רוחבי | — |
| Cache (4) | T-501 | 0.5 |
| בדיקות (6) | E6 | ~1.5 |
| תיעוד והגשה (4) | E9 | 1.5 |

*(יש חפיפה בין התחומים. השעות מצביעות על סדר גודל ולא על חלוקה מדויקת.)*
