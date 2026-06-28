# Tổng quan Kiến trúc và Workflow Dự án NovelTTS

Dự án NovelTTS là một ứng dụng WPF (C# .NET 4.7.2) thực hiện việc crawl dữ liệu truyện chữ từ website (như `truyenfull.today`), làm sạch nội dung, gộp chương và chuyển đổi văn bản đó thành âm thanh (Text-to-Speech).

Dự án tuân theo kiến trúc Clean MVVM với luồng xử lý bất đồng bộ (Asynchronous Design) và đa luồng (Multi-threading). Việc quản lý trạng thái được thiết kế bằng SQLite để đảm bảo khả năng tạm dừng (Pause), tiếp tục (Resume) an toàn.

## 1. Thành phần chính (Core Components)

1. **Crawler Pipeline** (`PipelineCoordinator`)
   - Chạy 3 thread chuyên biệt liên kết với nhau qua `BlockingCollection`:
     - **List Crawler**: Lấy danh sách chương từ trang web và đẩy vào Download Queue.
     - **Downloader**: Tải file HTML chi tiết từ Download Queue và đẩy vào Parse Queue.
     - **Parser**: bóc tách nội dung HTML (lọc quảng cáo, script) và lưu ra file `.txt`.
2. **Filter + Merge** (`MergeService`)
   - Lọc các file `.txt` đã được tải (ParseStatus = Completed).
   - Gộp lại thành các file lớn (Batching/Chunking) để phục vụ việc nghe liên tục mà không bị cắt vụn.
3. **Convert Audio / TTS** (`TtsService`)
   - Chạy đa luồng song song (Worker pool) nhằm tăng tốc độ render âm thanh.
   - Sử dụng `System.Speech.Synthesis` chuyển đổi từ Text sang file `.wav`, rồi dùng `NAudio.Lame` nén lại thành `.mp3`.

## 2. Quản lý trạng thái (State Management)
Toàn bộ tiến trình đều được lưu xuống csdl SQLite (sử dụng Entity Framework/Dapper) với các trạng thái (`Pending`, `InProgress`, `Completed`, `Failed`). Điều này giúp cho việc debug và resume an toàn:
- Nếu ứng dụng dừng đột ngột, có thể resume lại chương đang tải dở.
- Quá trình TTS có thể bỏ qua file mp3 đã render hoàn chỉnh.

## 3. Thư mục Logs và Text/Audio
- File Text được lưu tại `[Thư mục Project]/Txt`
- File Gộp được lưu tại `[Thư mục Project]/Merged`
- File MP3 được lưu tại `[Thư mục Project]/Audio`
- Logs sẽ ghi chi tiết từng ngoại lệ phục vụ debug.

Workflow chi tiết của từng Phase (Crawl, Merge, TTS) vui lòng tham khảo các file `.puml` tương ứng ở trong thư mục này.
