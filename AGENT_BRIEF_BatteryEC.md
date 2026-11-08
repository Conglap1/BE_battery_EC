# CONTEXT HANDOFF — BatteryEC Backend

## 1. Mục tiêu
Đọc tài liệu này để nắm bối cảnh dự án trước khi phân tích hoặc chỉnh sửa source. Không tự ý viết lại kiến trúc hay refactor hàng loạt. Trước khi sửa bất kỳ vấn đề nào, hãy mở file thực tế, xác minh nhận định, ghi rõ file/dòng code liên quan và đánh giá ảnh hưởng.

## 2. Tổng quan dự án
- Repository: https://github.com/Conglap1/BE_battery_EC
- Backend: ASP.NET Core Web API, .NET 8
- Database: PostgreSQL 16
- ORM / data access: Entity Framework Core và Dapper
- Kiến trúc mục tiêu: Clean Architecture, kết hợp Service Layer và một số khái niệm DDD
- Docker Compose được dùng để chạy PostgreSQL.
- Các nghiệp vụ chính nhìn thấy trong source: Catalog/Products, Inventory, Sales/Orders, POS, Production/Assembly, Purchasing, Repairs/Services, Warranty, Reports, Authentication/Authorization.

## 3. Cấu trúc solution
Solution có 4 project chính:

1. `BatteryEC.Domain`
   - Entities, Enums, Exceptions, Value Objects, Domain Events, Aggregate Root và Repository interfaces.
   - Domain là lõi nghiệp vụ, không được phụ thuộc ASP.NET Core, EF Core, Dapper hoặc Infrastructure.

2. `BatteryEC.Application`
   - Services/use cases, DTOs, application interfaces, validators và DependencyInjection.
   - Điều phối nghiệp vụ và định nghĩa các abstraction cần thiết.
   - Không truy cập trực tiếp `DbContext`, SQL hoặc PostgreSQL.

3. `BatteryEC.Infrastructure`
   - EF Core `BatteryDbContext`, Entity Configurations, Repository implementations, UnitOfWork, Dapper queries/connection factory, JWT/security và các dịch vụ kỹ thuật.
   - Triển khai interface được định nghĩa ở Application/Domain.

4. `BatteryEC.WebApi`
   - Controllers, middleware, `Program.cs`, cấu hình API và Composition Root.
   - Controller nên nhận request, gọi Application, trả response; không chứa business logic và không truy cập trực tiếp DbContext.

Quan hệ tham chiếu mong muốn/đã ghi nhận:
- Domain: không tham chiếu project khác.
- Application → Domain.
- Infrastructure → Application + Domain.
- WebApi → Application; WebApi có thể tham chiếu Infrastructure tại Composition Root để đăng ký dependency injection.
- Dependency phải hướng vào trong; Domain không phụ thuộc các tầng bên ngoài.

## 4. Phân chia EF Core và Dapper
- EF Core: thao tác dữ liệu nghiệp vụ, entity tracking, CRUD, repository và transaction.
- Dapper: các truy vấn đọc/tổng hợp/report hoặc SQL phức tạp cần kiểm soát trực tiếp.
- Tránh để EF Core và Dapper cùng xử lý một use case mà không có quy ước rõ ràng.
- Với Dapper, kiểm tra parameterization, cancellation token, connection lifetime và transaction consistency.

## 5. Các vấn đề cần xác minh trong source
Các điểm dưới đây là kết quả review sơ bộ từ ZIP. Hãy xác minh lại trên code hiện tại trước khi kết luận hoặc sửa.

### P0 — Bảo mật và toàn vẹn dữ liệu
1. `ProductsController.cs` có dấu hiệu inject/truy cập trực tiếp `BatteryDbContext`. Nếu vẫn đúng, chuyển truy vấn qua Application service/use case và abstraction phù hợp. Controller không nên phụ thuộc DbContext.
2. API sản phẩm có `[AllowAnonymous]` nhưng DTO/response mapping có thể chứa `CostPrice`. Kiểm tra endpoint công khai và loại bỏ giá vốn/thông tin nội bộ khỏi public response. Tách public DTO và admin/management DTO.
3. `OrdersController.cs` có logic cho phép giữ `dto.UserId` nếu client gửi giá trị dương, chỉ lấy current user khi UserId không hợp lệ. Không tin UserId từ client đối với người dùng đã xác thực. Lấy user ID từ JWT/`ICurrentUserService`; kiểm tra quyền sở hữu khi đọc đơn hàng.
4. Một số endpoint nghiệp vụ có thể nhận `userId`/`cashierUserId` từ query string hoặc dùng mặc định `1` (ví dụ điều chỉnh kho, xác nhận/giao đơn, POS, sửa chữa). Actor ID phải lấy từ danh tính đã xác thực, không nhận tùy ý từ client. Kiểm tra quyền theo role/permission.
5. `InventoryService.cs`: kiểm tra mọi quantity phải > 0 ở các thao tác reserve/release/adjust; không cho phép số âm làm sai tồn kho. Kiểm tra trường hợp inventory không tồn tại.
6. Kiểm tra `CreateOrderAsync` có reserve/validate tồn kho đầy đủ và nguyên tử hay không. Xử lý race condition khi nhiều request đồng thời đặt cùng sản phẩm. Kiểm tra `Inventory.Version` có được cấu hình làm concurrency token và được sử dụng đúng không.
7. Kiểm tra tất cả transaction paths trong `InventoryService` và `OrderService` (đặc biệt Complete/Cancel): mọi nhánh sau BeginTransaction phải Commit hoặc Rollback/Dispose an toàn; không return sớm để bỏ transaction dang dở.

### P1 — Đúng kiến trúc và dữ liệu
8. `AuthService.cs` được ghi nhận đang nằm trong Infrastructure và xử lý cả use case đăng ký/đăng nhập/refresh token. Đánh giá việc tách orchestration/use case vào Application; Infrastructure chỉ triển khai password/token/user persistence abstractions.
9. Rà soát Entity Configurations có `Ignore` các property như `Product.Brand`, `ShortDescription`, `IsFeatured`, hoặc variant `OptionSummary`, `Unit`, `ImageUrl`, trong khi API mapping vẫn trả các thuộc tính này. Xác định chúng là computed/not-mapped hay dữ liệu cần persist; đồng bộ Domain, EF mapping, schema và DTO.
10. Kiểm tra migrations/schema/database setup. Review sơ bộ không thấy đầy đủ EF Core migrations hoặc SQL scripts cho PostgreSQL views/triggers mà Dapper/report queries sử dụng. Xác minh các objects như `vw_total_revenue_summary`, `vw_supplier_debt_summary`, `vw_inventory_reconciliation`, `vw_warranty_lookup` và trigger `trg_users_link_customer_on_register`. Đảm bảo người clone repo có thể dựng DB từ đầu bằng hướng dẫn/scripts rõ ràng.
11. `appsettings.json` có thể chứa JWT secret hard-coded. Nếu đúng, coi secret đã lộ vì repository public: loại khỏi source, rotate secret đã từng dùng, sử dụng User Secrets/Environment Variables/secret manager theo môi trường.
12. Refresh token có dấu hiệu được lưu dạng chuỗi ghép bằng dấu `;` trong trường User. Đánh giá rủi ro revoke, multi-device, expiry và replay. Cân nhắc bảng session/refresh-token riêng, lưu hash, expiry, revoked/replaced metadata.
13. `IDateTimeProvider` có thể được khai báo nhưng chưa đăng ký/sử dụng, trong khi service gọi `DateTime.UtcNow` trực tiếp. Kiểm tra và thống nhất cách quản lý thời gian để dễ test.
14. Rà soát trạng thái nghiệp vụ: nếu đã có Enum nhưng Entity/DB/service vẫn dùng string tự do, cân nhắc thống nhất mapping và validation; không đổi schema tùy tiện nếu ảnh hưởng dữ liệu hiện có.

## 6. Những điểm nên giữ nếu không có bằng chứng cần thay đổi
- Bốn project Domain/Application/Infrastructure/WebApi.
- Domain độc lập.
- Application phụ thuộc Domain.
- Infrastructure triển khai repository và service abstractions.
- Repository + Unit of Work nếu đang giải quyết nhu cầu thực tế và transaction được quản lý đúng.
- Entity Configurations tách khỏi DbContext.
- Dùng EF Core cho nghiệp vụ ghi và Dapper cho read/report query phù hợp.
- DTO riêng cho request/response.
- DI đăng ký qua `AddApplication()` và `AddInfrastructure(configuration)`.
- CancellationToken trong các thao tác async.

Không thêm MediatR, CQRS, AutoMapper, Domain Events hoặc abstraction mới chỉ vì xu hướng. Chỉ bổ sung khi có lợi ích cụ thể và phù hợp quy mô dự án.

## 7. Quy trình làm việc yêu cầu với Agent
1. Đọc solution, `.csproj`, toàn bộ folder `src`, `tests`, `Program.cs`, DI registrations, DbContext, Entity Configurations và database scripts.
2. Xác minh từng finding ở mục 5 bằng file path + đoạn code/dòng code. Đánh dấu: confirmed / not reproducible / needs more context.
3. Chạy `dotnet build` và `dotnet test` nếu môi trường có .NET SDK và database/test dependencies. Báo rõ lệnh nào đã chạy, kết quả nào thành công/thất bại; không được nói test pass nếu chưa chạy.
4. Vẽ dependency graph và chọn ít nhất một luồng end-to-end (ví dụ Create Product, Create Order hoặc Adjust Stock) để chỉ rõ Controller → Application → Domain/Interfaces → Infrastructure → PostgreSQL.
5. Xếp lỗi theo mức độ: security/data integrity trước, architecture/design sau, style/refactor cuối.
6. Trước khi sửa, đưa kế hoạch nhỏ, liệt kê file sẽ thay đổi và rủi ro. Ưu tiên sửa từng nhóm nhỏ, không rewrite toàn bộ.
7. Sau mỗi thay đổi, build/test lại và tóm tắt: vấn đề, nguyên nhân, file đã sửa, cách kiểm chứng, phần còn tồn tại.
8. Không thay đổi API contract, database schema, migration, authentication behavior hoặc business rule nếu chưa phân tích backward compatibility và tác động dữ liệu.

## 8. Mục tiêu ưu tiên
Ưu tiên thực hiện theo thứ tự:
1. Chống giả mạo user/actor ID và kiểm tra quyền sở hữu dữ liệu.
2. Không lộ CostPrice hoặc dữ liệu nội bộ trên API public.
3. Sửa validation tồn kho, concurrency và transaction safety.
4. Loại bỏ truy cập DbContext trực tiếp từ Controller.
5. Đồng bộ Entity, EF configuration, DTO và database schema.
6. Chuẩn hóa Auth use cases và refresh-token lifecycle.
7. Bổ sung quy trình dựng database đầy đủ (migrations, views, triggers, seed nếu cần).
8. Bổ sung/hoàn thiện tests cho các quy tắc nghiệp vụ và lỗi bảo mật nêu trên.

## 9. Yêu cầu đầu ra đầu tiên
Trước khi code, hãy trả về một báo cáo ngắn gồm:
- Cấu trúc solution và dependency graph thực tế.
- Findings nào đã xác minh, kèm file path/dòng code.
- Build/test status thực tế.
- Top 5 rủi ro ưu tiên.
- Kế hoạch sửa theo từng bước, không tự ý thực hiện refactor diện rộng.
