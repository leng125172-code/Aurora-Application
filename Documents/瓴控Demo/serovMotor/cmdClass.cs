namespace serovMotor;

public static class cmdClass
{
	public const byte CMD_LEAST_FRAME_SIZE = 5;

	public const byte CMD_HEAD = 62;

	public const byte CMD_WRITE_PID_RAM = 49;

	public const byte CMD_READ_MOTOR_ANGLE = 146;

	public const byte CMD_CLEAR_MOTOR_LOOPS = 147;

	public const byte CMD_READ_MOTOR_SINGLE_ANGLE = 148;

	public const byte CMD_SET_MOTOR_ZERO_RAM = 149;

	public const byte CMD_READ_MOTOR_STATE1_ERROR = 154;

	public const byte CMD_CLEAR_MOTOR_STATE1_ERROR = 155;

	public const byte CMD_READ_MOTOR_STATE2 = 156;

	public const byte CMD_READ_MOTOR_STATE3 = 157;

	public const byte CMD_READ_PARAMETER = 64;

	public const byte CMD_WRITE_PARAMETER_TO_RAM = 66;

	public const byte CMD_WRITE_PARAMETER_TO_ROM = 68;

	public const byte CMD_RESET_SETTING_PARAMETER = 72;

	public const byte CMD_RESET_CALIB_PARAMETER = 74;

	public const byte CMD_MOTOR_OFF = 128;

	public const byte CMD_MOTOR_ON = 136;

	public const byte CMD_MOTOR_STOP = 129;

	public const byte CMD_MOTOR_RESTORE = 137;

	public const byte CMD_OFF_BRAKE_CONTROL = 140;

	public const byte CMD_OPEN_CONTROL = 160;

	public const byte CMD_TORQUE_CONTROL = 161;

	public const byte CMD_SPEED_CONTROL = 162;

	public const byte CMD_ANGLE_CONTROL1 = 163;

	public const byte CMD_ANGLE_CONTROL2 = 164;

	public const byte CMD_ANGLE_CONTROL3 = 165;

	public const byte CMD_ANGLE_CONTROL4 = 166;

	public const byte CMD_ANGLE_CONTROL5 = 167;

	public const byte CMD_ANGLE_CONTROL6 = 168;

	public const byte CMD_READ_DEVICE_TYPE = 31;

	public const byte CMD_REBOOT_DEVICE = 7;

	public const byte CMD_CONNECT = 16;

	public const byte CMD_DISCONNECT = 17;

	public const byte CMD_READ_INFO = 18;

	public const byte CMD_READ_SETTING = 20;

	public const byte CMD_WRITE_SETTING = 21;

	public const byte CMD_READ_CALIB = 22;

	public const byte CMD_WRITE_CALIB = 23;

	public const byte CMD_CALIBRATE = 24;

	public const byte CMD_SET_OFFSET = 25;

	public const byte CMD_REDUCER_MOTOR_ALIGN = 32;

	public const byte CMD_BEGIN_IAP = 187;
}
