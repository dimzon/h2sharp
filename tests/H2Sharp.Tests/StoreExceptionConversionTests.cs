/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 229: the reference's Store.convertMVStoreException maps every MVStore failure code to
 * a database error code, with a different answer for a corrupted encrypted file.
 */
using H2Sharp.Api;
using H2Sharp.MvStore;
using Xunit;
using MvStoreDbStore = H2Sharp.MvStore.Db.Store;

namespace H2Sharp.Tests;

public sealed class StoreExceptionConversionTests
{
    [Theory]
    [InlineData(DataUtils.ErrorClosed, ErrorCode.DatabaseIsClosed)]
    [InlineData(DataUtils.ErrorUnsupportedFormat, ErrorCode.FileVersionError1)]
    [InlineData(DataUtils.ErrorFileCorrupt, ErrorCode.FileCorrupted1)]
    [InlineData(DataUtils.ErrorFileLocked, ErrorCode.DatabaseAlreadyOpen1)]
    [InlineData(DataUtils.ErrorReadingFailed, ErrorCode.IoException1)]
    [InlineData(DataUtils.ErrorWritingFailed, ErrorCode.IoException1)]
    [InlineData(12345, ErrorCode.GeneralError1)]
    public void ConvertsMvStoreCodes(int storeCode, int databaseCode)
    {
        H2SharpException converted = MvStoreDbStore.ConvertMvStoreException(
            new MvStoreException(storeCode, "failure"), "test.db", encrypted: false);
        Assert.Equal(databaseCode, converted.H2ErrorCode);
    }

    [Fact]
    public void CorruptedEncryptedFileIsReportedAsAnEncryptionError()
    {
        H2SharpException converted = MvStoreDbStore.ConvertMvStoreException(
            new MvStoreException(DataUtils.ErrorFileCorrupt, "failure"), "test.db", encrypted: true);
        Assert.Equal(ErrorCode.FileEncryptionError1, converted.H2ErrorCode);
    }
}
