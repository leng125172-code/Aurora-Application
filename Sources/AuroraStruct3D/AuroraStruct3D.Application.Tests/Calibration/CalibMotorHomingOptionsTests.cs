using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Calibration;
using AuroraStruct3D.Leisai.Dtos;
using AuroraStruct3D.Leisai;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

public class CalibMotorHomingOptionsTests
{
    [Fact]
    public void HomingInput_RequiresStopPosition_WhenMoveAfterHomeIsEnabled()
    {
        LeisaiHomingConfigInputDto input = new() { MoveAfterHome = true };
        List<ValidationResult> results = new();

        bool valid = Validator.TryValidateObject(input, new ValidationContext(input), results, true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(input.HomeStopPosition)));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void HomingInput_AcceptsInt32StopPosition(int stopPosition)
    {
        LeisaiHomingConfigInputDto input = new()
        {
            MoveAfterHome = true,
            HomeStopPosition = stopPosition,
        };

        Assert.True(Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }

    [Fact]
    public void MotorParam_PersistsHomingOptions()
    {
        CalibMotorParam param = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        param.SetHomingOptions(moveAfterHome: true, withZSignal: true);

        Assert.True(param.MoveAfterHome);
        Assert.True(param.WithZSignal);
    }

    [Theory]
    [InlineData(0, 0x0000, 0x0000)]
    [InlineData(1, 0x0000, 0x0001)]
    [InlineData(-1, 0xFFFF, 0xFFFF)]
    [InlineData(int.MinValue, 0x8000, 0x0000)]
    [InlineData(int.MaxValue, 0x7FFF, 0xFFFF)]
    public void SplitInt32_UsesHighWordThenLowWord(int value, ushort high, ushort low)
    {
        Assert.Equal((high, low), LeisaiMotorAppService.SplitInt32(value));
    }
}
