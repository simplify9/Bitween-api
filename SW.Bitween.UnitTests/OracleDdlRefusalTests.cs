using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Adapters.Db.Oracle;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Oracle runs DDL as soon as DBMS_SQL.PARSE sees it, so the statement check refuses DDL before
/// parsing. These pin which first word is read, past the whitespace and comments an operator
/// might put in front of it.
/// </summary>
[TestClass]
public class OracleDdlRefusalTests
{
    [DataTestMethod]
    [DataRow("drop table orders", "drop")]
    [DataRow("  \n\tTRUNCATE TABLE orders", "TRUNCATE")]
    [DataRow("-- tidy up\nDROP TABLE orders", "DROP")]
    [DataRow("/* why */ grant select on orders to public", "grant")]
    [DataRow("select * from orders", "select")]
    [DataRow("begin null; end;", "begin")]
    [DataRow("", null)]
    [DataRow("-- only a comment", null)]
    public void The_leading_keyword_is_read_past_whitespace_and_comments(string sql, string expected) =>
        Assert.AreEqual(expected, OracleDbAdapter.LeadingKeyword(sql));
}
