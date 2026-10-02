namespace WinTabber.UI.Common.Tests.AccessKeys;

// AccessKeyBadgeLayer itself cannot be constructed headless (it now owns an AccessKeyOverlayWindow,
// a real WinUI window) -- the same limitation previously hit with Popup construction. Its one piece
// of pure logic (position math) now lives on AccessKeyOverlayWindow and is tested there --
// see AccessKeyOverlayWindowTests.cs. This file is intentionally empty of test classes; kept only so
// a future addition to AccessKeyBadgeLayer that IS pure logic has an obvious home.
