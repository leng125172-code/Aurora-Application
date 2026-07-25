namespace FringeTool;

public static class TjProjectorCommands
{
    public const int TcpPort = 4000;
    public const int SetModeOffsetBase = '0';

    public const string ReadVersion = "V\r\n";
    public const string LedOn = "L1\r\n";
    public const string LedOff = "L0\r\n";
    public const string SetLightPrefix = "B";
    public const string ColorRed = "CR\r\n";
    public const string ColorGreen = "CG\r\n";
    public const string ColorBlue = "CB\r\n";
    public const string ColorWhite = "CW\r\n";
    public const string TriggerOnce = "T\r\n";
    public const string TriggerWithGrayPrefix = "G ";
    public const string TriggerNextFrame = "N\r\n";
    public const string ReadPixelMode = "Fp\r\n";
    public const string SetFringeOrientationBitmapPrefix = "MF ";
    public const string SetImageCountPrefix = "MB ";
    public const string SetImageRepeatPrefix = "MA ";
    public const string SaveParams = "MS";
    public const string SaveFringeParams = "Ms";
    public const string EraseFlash = "FE";
    public const string FlashEraseOk = "F0";
    public const string WriteFlashPixelPrefix = "FW";
    public const string SoftReset = "X";
    public const string SetFlipPrefix = "FL ";
    public const string SetTriggerModePrefix = "TM ";
    public const string SetBootImagePrefix = "BI ";
    public const string SetCheckerboardPixelPrefix = "CK ";
    public const string SetRgbColorPrefix = "LE ";
    public const string ReadRegisterPrefix = "R ";
    public const string WriteRegisterPrefix = "W ";
}