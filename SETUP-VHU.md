# Photobooth VHU — Thiết lập bản cập nhật

## Trạng thái bàn giao

Bản cập nhật triển khai giao diện, cấu hình, renderer, trang trí sau chụp, adapter Canon EDSDK, nguồn HDMI/UVC và dịch vụ Drive/QR. Theo yêu cầu chủ dự án, chưa chạy ứng dụng, chưa restore/build và chưa chạy kiểm thử. Không xem mã nguồn này là bản đã nghiệm thu phần cứng hoặc sẵn sàng dùng ngay tại sự kiện.

Không đính kèm DLL độc quyền của Canon, thông tin đăng nhập Google hoặc bộ frame chính thức của trường. Các file `bin/` và `obj/` có sẵn từ repository cũ không đại diện cho mã nguồn cập nhật này; không chạy EXE cũ để đánh giá thay đổi.

## Mở dự án

- Windows x64, .NET 10 SDK với WPF desktop workload.
- Mở `PhotoboothStarter.sln`; project chạy là `Photobooth.App`, đích x64.
- Khi chủ dự án cho phép chạy/build, IDE cần khôi phục các package được khai báo trong project.
- Mặc định mở ở **Giả lập**: nguồn preview minh họa và ảnh chụp JPEG 6000×4000 có nhãn giả lập, không cần Canon.
- Bấm **Kết nối** rồi chọn bố cục/màu và **BẮT ĐẦU CHỤP (4 TẤM)**.

## Quy tắc sản phẩm

- Đúng 4 ảnh; `Grid2x2` hoặc `VerticalStrip`, không còn chọn 6 ảnh.
- AF bắt đầu ở số 1, kích chụp khi về 0, đợi JPEG tải xong mới đếm ảnh tiếp theo. Nếu AF trả lỗi thì dừng phiên; thời điểm màn trập thực tế vẫn do máy ảnh/SDK quyết định.
- Sau ảnh thứ 4, trung tâm hiển thị ảnh ghép có màu/frame, tự mở tab sticker. Sticker có sẵn; có thể thêm PNG, kéo di chuyển, kéo góc đổi kích thước và bấm × để xóa.
- Bấm **Hoàn tất & Nhận mã QR** mới xuất và bắt đầu chờ Drive.
- Tạo đúng 5 file: `photo_01.jpg`–`photo_04.jpg` và `photobooth_vhu.jpg`, trong thư mục phiên `yyyyMMdd_HHmmss`. Nếu tên đã tồn tại trên máy, chọn giây kế tiếp còn trống, không ghi đè phiên cũ.
- Ảnh đơn: 4200×2800, màu/LUT + frame đơn; không có sticker.
- Ảnh ghép lưới: 8400×5600. Dải dọc: 4200×11200. Canvas hiện không thêm gutter/footer; mỗi ô vẫn là ảnh ngang 3:2. Ảnh ghép áp frame bố cục và sticker, không lặp frame đơn vào từng ô.
- 70% là kích thước **mỗi chiều** từ nguồn 6000×4000. JPEG Quality riêng, mặc định 90, có thể chỉnh trong Cài đặt. Không có cam kết thời gian đồng bộ.
- Sau khi xuất thành công, bố cục sticker của phiên bị khóa để liên kết QR luôn chỉ đến đúng ảnh đã xác nhận. Có thể mở lại/thử lại QR hoặc bắt đầu phiên mới.

## Frame và sticker

Lần khởi động đầu, ứng dụng tạo PNG mặc định trong `%LOCALAPPDATA%\PhotoboothVHU\Frames` và thư viện PNG trong `%LOCALAPPDATA%\PhotoboothVHU\Stickers`.

Ba khung mặc định là đồ họa tạm xanh dương có chữ VHU, không dùng logo chính thức và không phải thiết kế đã được trường phê duyệt. Ba sticker mặc định là chữ VHU, ngôi sao và trái tim, được vẽ bằng vector trong ứng dụng rồi lưu thành PNG.

Trong **Cài đặt**, chọn độc lập ba PNG:

| Loại | Tỷ lệ | Canvas xuất |
| --- | --- | --- |
| Ảnh đơn | 3:2 | 4200×2800 |
| Lưới 2×2 | 3:2 | 8400×5600 |
| Dải dọc 1×4 | 3:8 | 4200×11200 |

Thiết kế PNG trong suốt, có tỷ lệ khớp canvas, các vùng hình trống theo 4 ô cùng kích thước. Đường dẫn khung do người vận hành chọn mà bị mất sẽ báo lỗi, không tự thay bằng khung khác.

Overlay live view crop giữa video về 3:2 để căn theo ảnh đơn. Crop giữa video không bảo đảm góc nhìn HDMI giống tuyệt đối JPEG của mọi chế độ Canon; cần căn thực tế khi được phép thử phần cứng.

## Canon R8 và HDMI capture card

1. Dùng micro HDMI → capture card → PC cho live view và USB tether riêng cho chụp/nhận JPEG.
2. Tải EDSDK Windows x64 chính thức từ Canon, đọc tài liệu và điều khoản đi kèm. Đặt `EDSDK.dll` và các DLL phụ thuộc **của cùng bộ SDK** cạnh EXE ứng dụng. Không đưa DLL vào GitHub; `.gitignore` đã loại tên DLL chính.
3. Adapter P/Invoke tập trung ở `Camera/CanonNative.cs`. Trước khi triển khai thực địa, cần đối chiếu chữ ký/struct/event với header của chính bộ SDK được cài và nghiệm thu trên R8; không có xác nhận ABI/phần cứng trong lần bàn giao này.
4. Trên máy ảnh, chọn USB **Photo Import/Remote Control**, đóng EOS Utility và các ứng dụng đang giữ camera. Cấu hình chụp ảnh JPEG Large 6000×4000, hướng ngang; tắt xoay tự động làm đổi kích thước pixel. Không chọn RAW-only.
5. Hiện adapter yêu cầu kết nối đúng một Canon. Chọn **Canon R8** trong Cài đặt và chọn đúng capture card DirectShow (có danh sách tên và chỉ số nhập tay). Bấm lưu rồi **Kết nối**.
6. Adapter dùng SDK trên một thread STA riêng có bơm sự kiện, nửa cò để AF và hết cò để chụp. Chỉ coi ảnh hoàn tất sau sự kiện truyền JPEG và ghi xong file. Có timeout 45 giây; sau lỗi truyền cần kết nối lại để tránh lấy nhầm ảnh trễ cho phiên mới.

Adapter đặt chế độ **SaveTo Host**, không cam kết lưu thêm bản sao trên thẻ SD. Giữ cấu hình máy ở JPEG-only; file RAW hoặc sự kiện ảnh không được yêu cầu sẽ bị hủy truyền. Không tự cài firmware hoặc thay đổi chế độ USB của máy ảnh.

Nguồn: [Canon SDK](https://asia.canon/en/campaign/developerresources/sdk), [hướng dẫn USB EOS R8](https://cam.start.canon/en/C013/manual/html/UG-08_Set-up_0270.html).

## Google Drive for desktop và OAuth

### Chuẩn bị

1. Cài và đăng nhập Google Drive for desktop. Tạo thư mục sự kiện trong Drive, ví dụ `G:\My Drive\PhotoboothVHU`, bảo đảm có quyền ghi.
2. Mở cùng thư mục đó trên web và sao chép **Folder ID** từ URL `https://drive.google.com/drive/folders/FOLDER_ID`. Thư mục cục bộ và Folder ID phải chỉ cùng một nơi.
3. Trong Google Cloud của người vận hành: bật Google Drive API, cấu hình consent screen và tạo OAuth client loại **Desktop App**. Nếu ứng dụng OAuth ở chế độ Testing, thêm tài khoản booth vào test users; refresh token có thể chịu chính sách hết hạn của Google.
4. Tải JSON OAuth về máy, giữ bên ngoài repository. Trong Cài đặt chọn JSON này, nhập đường dẫn lưu/Folder ID và bấm **Đăng nhập Google**. Ứng dụng mở trình duyệt để người vận hành đăng nhập và cấp quyền, dùng loopback redirect + PKCE + state.
5. Lưu cài đặt. Dùng cùng tài khoản với Drive for desktop, có quyền chia sẻ thư mục sự kiện. Chính sách Workspace có thể chặn anyone-with-link; ứng dụng sẽ báo lỗi thay vì tạo QR giả.

### Quyền và dữ liệu

Ứng dụng yêu cầu scope `https://www.googleapis.com/auth/drive` để tra cứu và chia sẻ các file do **Drive for desktop** tạo. Scope `drive.file` không mặc nhiên truy cập các file bên ngoài ứng dụng. OAuth Desktop JSON là cấu hình client, refresh token được mã hóa bằng Windows DPAPI cho người dùng hiện tại tại `%LOCALAPPDATA%\PhotoboothVHU\google.tokens`; không ghi access/refresh token vào log hoặc mã nguồn. Có thể thu hồi quyền trên trang tài khoản Google; sau đó đăng nhập lại nếu cần dùng tiếp.

Thư mục phiên được chia sẻ **reader, anyone with link**, không cho tìm kiếm công khai bằng tùy chọn của ứng dụng. Người có URL có thể xem ảnh; thông tin này được hiển thị trong Cài đặt. Nếu thư mục cha đã có quyền rộng hơn thì ứng dụng không tự thu hẹp các quyền kế thừa.

### Luồng đồng bộ

- Không upload file qua API: Drive for desktop chịu trách nhiệm tải lên.
- API tìm thư mục phiên trong **đúng parent ID**, từ chối nếu nhiều thư mục cùng tên.
- Chờ đúng 5 file khớp tên, dung lượng và MD5 với bản cục bộ, có phân trang. Không coi thấy thư mục là đã tải xong ảnh.
- Sau đó cấp/kiểm tra quyền chia sẻ và lấy `webViewLink` do Google trả về để tạo QR.
- Popup Loading hiển thị tiến trình; timeout mặc định 180 giây, có thể đổi. Khi lỗi, có **Thử lại**, không yêu cầu chụp lại và không tạo thư mục mới.
- Đóng popup hủy chờ, giữ ảnh trên máy. Có thể mở lại QR cho phiên đang hiển thị. Chưa có hàng đợi khôi phục QR qua lần khởi động lại; thư mục ảnh đã xuất vẫn còn trên Drive/đĩa.

Nguồn: [tìm file và thư mục](https://developers.google.com/workspace/drive/api/guides/search-files), [quản lý chia sẻ](https://developers.google.com/workspace/drive/api/guides/manage-sharing), [OAuth ứng dụng desktop](https://developers.google.com/identity/protocols/oauth2/native-app), [QRCoder](https://github.com/Shane32/QRCoder).

## Lưu trữ và vận hành

- `settings.json`, frame, sticker, token mã hóa và JPEG nguồn nằm trong `%LOCALAPPDATA%\PhotoboothVHU`.
- JPEG nguồn được giữ trong `Working/<id>/`, tách khỏi thư mục chia sẻ 5 file. Hiện không tự dọn nguồn; người vận hành cần lên lịch dọn sau khi xác nhận đã giữ ảnh cần thiết.
- File xuất được mã hóa vào `.partial`, rồi đổi sang `.jpg` sau khi hoàn tất. QR chỉ được tạo khi có đủ 5 JPG đã khớp trên cloud.
- Không chạy đồng thời nhiều instance vào cùng thư mục sự kiện. Không thay tài nguyên hoặc file kết quả của phiên trong lúc chờ đồng bộ.
- Ảnh ghép 70% vẫn khá lớn, đặc biệt dải dọc. Cần đánh giá bộ nhớ/tốc độ trên máy booth và kết nối thực trước sự kiện khi được phép chạy thử.

## Những việc cần làm khi được phép nghiệm thu

Chưa thực hiện các bước này trong lần cập nhật theo yêu cầu không chạy/kiểm thử: khôi phục package và build x64; chạy hai layout ở chế độ giả lập; xác nhận frame/sticker khớp file xuất; đối chiếu header EDSDK, AF và JPEG trên Canon R8; rút/cắm thiết bị; thử mạng chậm, chính sách chia sẻ và quét QR bằng điện thoại không đăng nhập tài khoản booth.
