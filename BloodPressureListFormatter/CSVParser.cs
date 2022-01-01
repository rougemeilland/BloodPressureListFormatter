using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BloodPressureListFormatter
{
    class CSVParser
        : IDisposable
    {
        private class CSVRowsSequence : IEnumerable<ICSVRow>
        {
            private class BufferForParser
            {

                private TextReader _reader;

                private int c1;
                private int c2;

                public BufferForParser(TextReader reader)
                {
                    _reader = reader;

                    c1 = _reader.Read();
                    c2 = c1 >= 0 ? _reader.Read() : -1;
                    currentPosition = 0;
                }

                public bool isEndOfReader()
                {
                    return c1 < 0;
                }

                public void skipNewLines()
                {
                    while (true)
                    {
                        if (startsWith("\r\n"))
                        {
                            drop(2);
                        }
                        else if (startsWith("\r") || startsWith("\n"))
                        {
                            drop(1);
                        }
                        else
                            return;
                    }
                }

                public bool startsWithNewLine()
                {
                    return c1 >= 0 && ((char)c1).IsAnyOf('\r', '\n');
                }

                public bool startsWith(string s)
                {
                    switch (s.Length)
                    {
                        case 1:
                            return c1 >= 0 && char.ConvertFromUtf32(c1) == s[0].ToString();
                        case 2:
                            return c1 >= 0 && char.ConvertFromUtf32(c1) == s[0].ToString() && c2 >= 0 && char.ConvertFromUtf32(c2) == s[1].ToString();
                        default:
                            throw new ArgumentException();
                    }
                }

                public void drop(int count = 1)
                {
                    for (var i = count; i > 0; --i)
                        readChar();
                }

                public char readChar()
                {
                    char result;
                    if (c1 >= 0)
                        result = (char)c1;
                    else
                        result = '\u0000';
                    c1 = c2;
                    c2 = _reader.Read();
                    ++currentPosition;
                    return result;
                }

                public int currentPosition { get; private set; } = 0;

                override public string ToString()
                {
                    string s;
                    if (c1 < 0)
                        s = "";
                    else if (c2 < 0)
                        s = ((char)c1).ToString();
                    else
                        s = string.Format("${0}${1}...", (char)c1, (char)c2);
                    return
                        string.Format("(pos={0}, stream='${1}", currentPosition, 1);
                }
            }

            private class ImplementOfCSVRow
                : ICSVRow
            {
                private string[] _columns;

                public ImplementOfCSVRow(string[] columns)
                {
                    _columns = columns;
                }

                public int size => _columns.Length;

                public string getString(int index) => _columns[index];

                public int? getInt(int index)
                {
                    int value;
                    if (int.TryParse(_columns[index], out value))
                        return value;
                    else
                        return null;
                }

                public long? getLong(int index)
                {
                    long value;
                    if (long.TryParse(_columns[index], out value))
                        return value;
                    else
                        return null;
                }

                public double? getDoule(int index)
                {
                    double value;
                    if (double.TryParse(_columns[index], out value))
                        return value;
                    else
                        return null;
                }

                public DateTime? getDate(int index)
                {
                    DateTime value;
                    if (!DateTime.TryParse(_columns[index], out value))
                        return null;
                    if (value.Kind == DateTimeKind.Unspecified)
                        value = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Local);
                    return value.ToLocalTime();
                }

                public override string ToString()
                {
                    return
                        string.Format(
                            "({0})",
                            string.Join(", ",
                            _columns
                                .Select(column => encode(column))));
                }

                private static string encode(string column)
                {
                    if (column.Any(c => c.IsAnyOf('\r', '\n', '\t', ',')))
                    {
                        var sb = new StringBuilder();

                        sb.Append("\"");
                        foreach (var c in column)
                        {

                            switch (c)
                            {
                                case '\r':
                                    sb.Append("\\r");
                                    break;
                                case '\n':
                                    sb.Append("\\n");
                                    break;
                                case '\t':
                                    sb.Append("\\t");
                                    break;
                                case '"':
                                    sb.Append("\\\"\\\"");
                                    break;
                                default:
                                    sb.Append(c);
                                    break;
                            }
                        }
                        sb.Append("\"");
                        return sb.ToString();
                    }
                    else
                        return column;
                }
            }

            private class CSVRowsIterator
                : IEnumerator<ICSVRow>
            {
                private string _filePath;
                private Encoding _encoding;
                private CSVDelimiter _delimiter;
                private TextReader _reader;
                private BufferForParser _parser;
                private ImplementOfCSVRow _currentValue;
                private bool _isDisposed;

                public CSVRowsIterator(string filePath, Encoding encoding, CSVDelimiter delimiter)
                {
                    _filePath = filePath;
                    _encoding = encoding;
                    _delimiter = delimiter;
                    _reader = new StreamReader(filePath, encoding);
                    _parser = new BufferForParser(_reader);
                    _parser.skipNewLines();
                    _currentValue = null;
                    _isDisposed = false;
                }

                public ICSVRow Current
                {
                    get
                    {
                        if (_isDisposed)
                            throw new ObjectDisposedException(string.Format("file: {0}", _filePath));
                        if (_currentValue == null)
                            throw new InvalidOperationException();
                        return _currentValue;
                    }
                }

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    if (_isDisposed)
                        throw new ObjectDisposedException(string.Format("file: {0}", _filePath));
                    if (_parser.isEndOfReader())
                        return false;
                    var columns = new List<string>();
                    while (!_parser.isEndOfReader())
                    {
                        columns.Add(parseColumn());
                        if (_parser.isEndOfReader())
                        {
                            _currentValue = new ImplementOfCSVRow(columns.ToArray());
                            return true;
                        }
                        else if (_parser.startsWithNewLine())
                        {
                            _parser.skipNewLines();
                            _currentValue = new ImplementOfCSVRow(columns.ToArray());
                            return true;
                        }
                        else if (_parser.startsWith(_delimiter.GetValue()))
                            _parser.drop();
                        else
                        {
                            // column の解析をした後の文字が、ファイルの終端、改行、カンマの何れでもない
                            throw new Exception("Internal error: column is not trailed ','");
                        }
                    }
                    _currentValue = new ImplementOfCSVRow(columns.ToArray());
                    return true;

                }

                public void Reset()
                {
                    if (_isDisposed)
                        throw new ObjectDisposedException(string.Format("file: {0}", _filePath));
                    if (_reader != null)
                    {
                        _parser = null;
                        _reader.Dispose();
                        _reader = null;
                    }
                    _reader = new StreamReader(_filePath, _encoding);
                    _parser = new BufferForParser(_reader);
                    _parser.skipNewLines();
                    _currentValue = null;
                }

                protected virtual void Dispose(bool disposing)
                {
                    if (!_isDisposed)
                    {
                        if (disposing)
                        {
                            if (_reader != null)
                            {
                                _parser = null;
                                _reader.Dispose();
                                _reader = null;
                            }
                        }
                        _isDisposed = true;
                    }
                }

                public void Dispose()
                {
                    Dispose(disposing: true);
                    GC.SuppressFinalize(this);
                }


                private string parseColumn()
                {
                    if (_parser.startsWithNewLine() || _parser.startsWith(_delimiter.GetValue()))
                        return "";
                    else
                    {
                        var sb = new StringBuilder();
                        return parseColumnUntilCommna(sb);
                    }
                }

                private string parseColumnUntilCommna(StringBuilder builder)
                {
                    while (!_parser.isEndOfReader())
                    {
                        if (_parser.startsWithNewLine() || _parser.startsWith(_delimiter.GetValue()))
                            break;
                        else if (_parser.startsWith("\"\""))
                        {
                            builder.Append("\"");
                            _parser.drop(2);
                        }
                        else if (_parser.startsWith("\""))
                            parseColumnUntilDoubleQuote(builder, _parser.currentPosition);
                        else
                        {
                            // この時点で、 index から始まる部分文字列は、空でもなく、デリミタでもなく、改行でもなく、ダブルクォートでもない
                            // 少なくとも最初の文字はエスケープ処理が不要なので、最初の一文字をまず builder に追加する
                            builder.Append(_parser.readChar());
                            while (
                                !_parser.isEndOfReader() &&
                                !_parser.startsWith("\"") &&
                                !_parser.startsWithNewLine() &&
                                !_parser.startsWith(_delimiter.GetValue())
                            )
                            {
                                builder.Append(_parser.readChar());
                            }
                        }
                    }
                    return builder.ToString();
                }

                private string parseColumnUntilDoubleQuote(StringBuilder builder, int startOfColumn)
                {
                    if (_parser.startsWith("\""))
                        throw new Exception("internal error");
                    _parser.drop();
                    while (!_parser.isEndOfReader())
                    {
                        if (_parser.startsWith("\"\""))
                        {
                            builder.Append("\"");
                            _parser.drop(2);
                        }
                        else if (_parser.startsWith("\""))
                        {
                            _parser.drop();
                            return builder.ToString();
                        }
                        else
                        {
                            while (!_parser.isEndOfReader() && !_parser.startsWith("\""))
                            {
                                builder.Append(_parser.readChar());
                            }
                            if (_parser.isEndOfReader())
                                throw new Exception("bad CSV format: column is not closed with double quotes: pos=$startOfColumn");
                            // この時点で、 _reader は '"' で始まっているはず
                            _parser.drop();
                        }
                    }
                    throw new Exception("bad CSV format: column is not closed with double quotes: pos=$startOfColumn");
                }
            }

            private string _filePath;
            private Encoding _encoding;
            private CSVDelimiter _delimiter;

            public CSVRowsSequence(string filePath, Encoding encoding, CSVDelimiter delimiter)
            {
                _filePath = filePath;
                _encoding = encoding;
                _delimiter = delimiter;
            }

            public IEnumerator<ICSVRow> GetEnumerator()
            {
                return new CSVRowsIterator(_filePath, _encoding, _delimiter);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private bool _isDisposed;
        private string _filePath;
        private Encoding _encoding;
        private CSVDelimiter _delimiter;

        public CSVParser(string filePath, Encoding encoding, CSVDelimiter delimiter)
        {
            _isDisposed = false;
            _filePath = filePath;
            _encoding = encoding;
            _delimiter = delimiter;
        }

        public IEnumerable<ICSVRow> GetRows()
        {
            return new CSVRowsSequence(_filePath, _encoding, _delimiter);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

    }
}