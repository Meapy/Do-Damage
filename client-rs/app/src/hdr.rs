//! Whether Windows shows a display in HDR, and how bright it draws ordinary (SDR) white there:
//! Options, Graphics, "HDR" starts on when it does.

/// With HDR on in Windows (Settings, Display, "Use HDR") on any display: the SDR white level
/// there, in scRGB units (1.0 is 80 nits; Windows' "SDR content brightness" slider). `None`
/// when no display is in HDR, or off Windows.
#[cfg(windows)]
pub fn windows_hdr() -> Option<f32> {
    use windows_sys::Win32::Devices::Display::*;
    use windows_sys::Win32::Foundation::ERROR_SUCCESS;
    let (mut paths, mut modes) = (0u32, 0u32);
    // SAFETY: the buffers are the sizes Windows asked for, and each packet's header gives its
    // own type and size.
    unsafe {
        if GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &mut paths, &mut modes) != ERROR_SUCCESS {
            return None;
        }
        let mut path_info: Vec<DISPLAYCONFIG_PATH_INFO> = vec![std::mem::zeroed(); paths as usize];
        let mut mode_info: Vec<DISPLAYCONFIG_MODE_INFO> = vec![std::mem::zeroed(); modes as usize];
        let found = QueryDisplayConfig(
            QDC_ONLY_ACTIVE_PATHS,
            &mut paths,
            path_info.as_mut_ptr(),
            &mut modes,
            mode_info.as_mut_ptr(),
            std::ptr::null_mut(),
        );
        if found != ERROR_SUCCESS {
            return None;
        }
        path_info.truncate(paths as usize);
        let mut best: Option<f32> = None;
        for path in &path_info {
            let target = &path.targetInfo;
            let mut color: DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO = std::mem::zeroed();
            color.header.r#type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
            color.header.size = std::mem::size_of::<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>() as u32;
            color.header.adapterId = target.adapterId;
            color.header.id = target.id;
            // Bit 1: advancedColorEnabled (HDR on).
            if DisplayConfigGetDeviceInfo(&mut color.header) != 0 || color.Anonymous.value & 2 == 0 {
                continue;
            }
            let mut white: DISPLAYCONFIG_SDR_WHITE_LEVEL = std::mem::zeroed();
            white.header.r#type = DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL;
            white.header.size = std::mem::size_of::<DISPLAYCONFIG_SDR_WHITE_LEVEL>() as u32;
            white.header.adapterId = target.adapterId;
            white.header.id = target.id;
            // SDRWhiteLevel is in thousandths of 80 nits; 1000 if it can't be read.
            let level = if DisplayConfigGetDeviceInfo(&mut white.header) == 0 { white.SDRWhiteLevel } else { 1000 };
            let paper = (level as f32 / 1000.0).clamp(1.0, 6.0);
            best = Some(best.map_or(paper, |b: f32| b.max(paper)));
        }
        best
    }
}

#[cfg(not(windows))]
pub fn windows_hdr() -> Option<f32> {
    None
}
