using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Pins <see cref="PlacesToMove.Read"/>: what a typed move on Mod Priority or Plugin Order means.</summary>
public class PlacesToMoveTests
{
	private static readonly PlacesToMove.Words English = new("up,u", "down,d", "top,first", "bottom,last");

	private static PlacesToMove.Move? Read(string typed, int from, int count, out PlacesToMove.Problem problem) =>
		PlacesToMove.Read(typed, from, count, English, out problem);

	[Theory]
	[InlineData("23 down", 10, 33)]
	[InlineData("down 23", 10, 33)]
	[InlineData("23down", 10, 33)]
	[InlineData("  23   Down ", 10, 33)]
	[InlineData("23 d", 10, 33)]
	[InlineData("5 up", 10, 5)]
	[InlineData("UP 5", 10, 5)]
	[InlineData("5 up.", 10, 5)]
	public void ANumberAndADirectionMoveThatManyPlaces(string typed, int from, int expected)
	{
		PlacesToMove.Move? move = Read(typed, from, 60, out PlacesToMove.Problem problem);

		Assert.Equal(PlacesToMove.Problem.None, problem);
		Assert.Equal(new PlacesToMove.Move(expected, false), move);
	}

	[Fact]
	public void UpIsTowardsTheTopOfTheList()
	{
		// On Mod Priority the top wins, so "up" must make the index smaller.
		Assert.Equal(9, Read("1 up", 10, 60, out _)?.Target);
		Assert.Equal(11, Read("1 down", 10, 60, out _)?.Target);
	}

	[Theory]
	[InlineData("36 up", 10, 0)]
	[InlineData("99 down", 10, 59)]
	[InlineData("99999999999999999999 down", 10, 59)]
	public void TooManyPlacesStopAtTheEnd(string typed, int from, int expected)
	{
		PlacesToMove.Move? move = Read(typed, from, 60, out _);

		Assert.Equal(new PlacesToMove.Move(expected, true), move);
	}

	[Fact]
	public void UpFromTheTopIsNoMoveRatherThanAnOvershoot()
	{
		// So the caller says "already at the top", not "moved to the top".
		Assert.Equal(new PlacesToMove.Move(0, false), Read("3 up", 0, 60, out _));
		Assert.Equal(new PlacesToMove.Move(59, false), Read("3 down", 59, 60, out _));
	}

	[Theory]
	[InlineData("12", 11)]
	[InlineData("1", 0)]
	[InlineData("60", 59)]
	public void ANumberOnItsOwnIsAPosition(string typed, int expected)
	{
		Assert.Equal(new PlacesToMove.Move(expected, false), Read(typed, 30, 60, out _));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("61")]
	public void APositionOutsideTheListIsRefused(string typed)
	{
		Assert.Null(Read(typed, 30, 60, out PlacesToMove.Problem problem));
		Assert.Equal(PlacesToMove.Problem.NoSuchPosition, problem);
	}

	[Theory]
	[InlineData("top", 0)]
	[InlineData("Bottom", 59)]
	[InlineData("first", 0)]
	[InlineData("last", 59)]
	public void TopAndBottomNeedNoNumber(string typed, int expected)
	{
		Assert.Equal(new PlacesToMove.Move(expected, false), Read(typed, 30, 60, out _));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("down")]
	[InlineData("23 sideways")]
	[InlineData("23 down 4")]
	[InlineData("-5")]
	[InlineData("five down")]
	[InlineData("3 top")]
	public void AnythingElseIsNotUnderstood(string typed)
	{
		Assert.Null(Read(typed, 30, 60, out PlacesToMove.Problem problem));
		Assert.Equal(PlacesToMove.Problem.NotUnderstood, problem);
	}

	[Fact]
	public void ATranslationsWordsAreUsed()
	{
		var german = new PlacesToMove.Words("hoch,rauf", "runter", "oben", "unten");

		Assert.Equal(7, PlacesToMove.Read("3 runter", 4, 60, german, out _)?.Target);
		Assert.Equal(0, PlacesToMove.Read("oben", 4, 60, german, out _)?.Target);
		Assert.Null(PlacesToMove.Read("3 down", 4, 60, german, out _));
	}
}
