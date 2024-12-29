# Dolby Vision Converter Tool

A tool to convert Dolby Vision `.mp4` and `.mkv` video files into the selected color space and codec.

![image](https://github.com/user-attachments/assets/39df9f00-f9c1-4491-b909-e2003bbb7223)

## Features

- **Drag and Drop Input**  
  Quickly drag your `.mp4` or `.mkv` file into the app (or choose the input/output via the browse buttons).

- **Dolby Vision → HDR10 / SDR / HLG**  
  Converts Dolby Vision files into three possible color spaces:
    - **HDR10**
    - **SDR**
    - **HLG**

- **Quality & Size Control via Bitrate**  
  Adjust the average bitrate (e.g., 15,000 kbps) to balance file size and video quality.

- **Codec Options: H.264 / H.265**  
  Choose between `libx264` (H.264) or `libx265` (H.265) for transcoding.

- **Automatic Output Naming**  
  The output file keeps the original name, appending `_converted` (plus extra details) at the end.
    - If no output folder is selected, it defaults to **the same folder** as your input.

## How It Works

- **Transcoding & Color Filters**  
  DVConverter uses [FFmpeg](https://ffmpeg.org/) with hardware-accelerated color filters (`libplacebo`) to decode Dolby Vision data, apply the correct tone mapping, and re-encode into your chosen color space.
    - The process **re-encodes** each frame, so expect some generation time depending on your CPU/GPU performance and bitrate choice.

- **Preserve Audio & Subtitles**  
  Audio and subtitle tracks are copied directly, so you won’t lose any of your original streams.

## Download & Usage

You can download the latest version of DVConverter from the [Releases Page](https://github.com/SlavomirDurej/DVConverter/releases).

### Instructions:
1. Download the latest `.zip` file from the [Releases Page](https://github.com/SlavomirDurej/DVConverter/releases).
2. Extract the contents of the `.zip` file to any folder on your computer.
3. Run the `DVConverter.exe` file to start the application.

Feel free to report any bugs or suggestions in the [Issues](https://github.com/SlavomirDurej/DVConverter/issues) tab.
