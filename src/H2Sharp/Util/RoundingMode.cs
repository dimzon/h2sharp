/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The rounding modes of java.math.RoundingMode.
 *
 * The names are the JDK's, because the modes are the JDK's and a mode spelled differently here
 * would be a mode spelled differently where it matters.
 *
 * A mode says what to do with the part that is thrown away. UPPARD and DOWNWARD are named for
 * their direction in the sense that the digit strings go, which is why their names in the JDK are
 * UP and DOWN; CEILING and FLOOR are named for the direction in the number line, which for a
 * negative number is the opposite way round. That difference is the whole reason a decimal cannot
 * round a negative value without knowing the sign, which is why the arithmetic here rounds the
 * magnitude and puts the sign back afterwards.
 */
namespace H2Sharp.Util;

/// <summary>
/// What to do with the digits that a calculation cannot keep.
/// </summary>
public enum RoundingMode
{
    /// <summary>
    /// Round away from zero, which is what makes every digit of the answer bigger in size.
    /// </summary>
    Up,

    /// <summary>
    /// Round towards zero, which makes no digit of the answer bigger in size.
    /// </summary>
    Down,

    /// <summary>
    /// Round towards positive infinity, so a negative value rounds the other way.
    /// </summary>
    Ceiling,

    /// <summary>
    /// Round towards negative infinity, so a negative value rounds the other way.
    /// </summary>
    Floor,

    /// <summary>
    /// Round away from zero when the discarded part is exactly half, and towards zero otherwise.
    /// </summary>
    HalfUp,

    /// <summary>
    /// Round towards zero when the discarded part is exactly half, and away from zero otherwise.
    /// </summary>
    HalfDown,

    /// <summary>
    /// Round towards the even digit when the discarded part is exactly half, and away from zero
    /// otherwise.
    /// </summary>
    HalfEven,


    /// <summary>
    /// Refuse: the calculation has to be exact.
    /// </summary>
    Unnecessary,
}
