# Tích hợp Canon EOS R8 cho Photobooth Windows

## Kết luận kỹ thuật

Canon EOS R8 được liệt kê trong danh sách máy ảnh hỗ trợ **Canon ED-SDK (EDSDK) V13.20.21** và **Camera Control API (CCAPI) V1.4.0 Rev. 1.5** của Canon South & Southeast Asia.

**Đề xuất cho photobooth:** ưu tiên EDSDK qua USB tether để điều khiển chụp, nhận ảnh độ phân giải cao và đọc Live View trong cùng một kết nối. Chỉ thêm HDMI capture nếu thử nghiệm thực tế cho thấy Live View từ SDK không đạt độ mượt/độ trễ yêu cầu. HDMI không thay thế được điều khiển chụp và tải ảnh qua SDK.

Nguồn chính thức:
- Danh sách SDK Canon South & Southeast Asia: https://asia.canon/en/campaign/developerresources/sdk
- Ghi chú phát hành EDSDK: https://asia.canon/en/campaign/developerresources/camera/cap/edsdk-eos-digital-camera-sdk-release-note
- Hướng dẫn EOS R8 chọn ứng dụng USB: https://cam.start.canon/en/C013/manual/html/UG-08_Set-up_0270.html

## Cấu hình body trước khi kết nối

1. Tắt EOS Utility và các ứng dụng khác đang giữ kết nối máy ảnh.
2. Trên EOS R8, vào mục cài đặt **Choose USB connection app** và chọn **Photo Import/Remote Control** khi dùng USB tether/EDSDK.
3. Không chọn **Video calls/streaming** để làm đường tether chính. Chế độ này dùng UVC/UAC, xuất video 1920×1080 ở 30 fps theo hướng dẫn máy ảnh; nó phù hợp làm nguồn webcam/streaming, không phải thay thế điều khiển máy ảnh EDSDK.
4. Dùng cáp USB-C có truyền dữ liệu, kết nối trực tiếp máy tính nếu có thể, hạn chế hub không cấp nguồn ổn định.
5. Đặt máy ở chế độ chụp ảnh; tắt hoặc tăng thời gian tự tắt nguồn. Cố định exposure/WB cho booth để bốn ảnh có màu nhất quán.
6. Trong giai đoạn thử, nên lưu ảnh vào thẻ SD và truyền bản sao về PC nếu workflow/SDK cho phép, để có bản dự phòng.

## Thiết kế adapter trong ứng dụng

Giữ UI không phụ thuộc SDK hãng:

- `ICameraController`: kết nối/ngắt kết nối, trạng thái, kích chụp, tải ảnh hoàn chỉnh.
- `ILiveViewSource` (nên tách riêng): bắt đầu/dừng Live View và trả frame để Filter Engine áp LUT.
- `CanonEdsdkCameraController`: adapter gọi EDSDK native, chỉ được bật khi SDK chính thức đã cài đúng phiên bản/kiến trúc.
- `HdmiLiveViewSource` (tuỳ chọn): đọc capture card như thiết bị UVC; chỉ cung cấp hình xem trước, không điều khiển chụp.

## Luồng chụp cần xử lý

1. Kiểm tra camera đang kết nối và sẵn sàng.
2. Trước mỗi lần chụp, yêu cầu autofocus/AF nếu cấu hình hỗ trợ; đợi kết quả hoặc timeout.
3. Gửi lệnh chụp.
4. Chờ sự kiện ảnh mới và tải xong file; không coi lệnh chụp trả về là dấu hiệu file đã tải xong.
5. Ghi file vào thư mục phiên chụp, kiểm tra file hợp lệ rồi mới chuyển sang shot tiếp theo.
6. Lặp đủ bốn ảnh, áp LUT lên ảnh gốc, sau đó ghép strip và xuất bốn ảnh riêng.
7. Nếu timeout, thẻ đầy, máy bận hoặc rút USB, dừng chuỗi chụp an toàn và hiển thị nút thử lại.

## Quan trọng khi triển khai

- Bộ mã nguồn Starter hiện tại vẫn dùng `DemoCameraController`; chưa có kết nối EDSDK thực.
- EDSDK là SDK native. Cần tải gói và tài liệu từ Canon, kiểm tra điều khoản phân phối, architecture x64, DLL phụ thuộc và callback theo mẫu của chính phiên bản SDK đang dùng.
- Không tự phân phối DLL SDK của Canon trong bộ cài nếu chưa được phép theo điều khoản của Canon.
- Trước khi dùng trong sự kiện, cần kiểm thử lặp nhiều chục/vài trăm phiên chụp, rút/cắm USB, mất kết nối, thẻ đầy, và thời gian chuyển ảnh RAW/JPEG.
