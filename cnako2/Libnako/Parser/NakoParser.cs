﻿using System;
using System.Collections.Generic;
using System.Text;
using Libnako.Parser.Node;
using Libnako.Parser.Tokenizer;

namespace Libnako.Parser
{
    /// <summary>
    /// トークンを読み込んで構文木に変換するクラス
    /// 再帰下降構文解析 LL(1)
    /// </summary>
    public class NakoParser : NakoParserBase
    {
        public NakoParser(NakoTokenList tokens) : base(tokens)
        {
        }

        /// <summary>
        /// 値を１つだけ解析したい場合
        /// </summary>
        /// <returns></returns>
        public Boolean ParseOnlyValue()
        {
            lastNode = null;
            if (tok.IsEOF()) return false;
            if (!_value()) return false;
            topNode.AddChild(lastNode);
            return true;
        }
        
        /// <summary>
        /// トークンを構文解析する
        /// </summary>
        public void Parse()
        {
            _program();
        }

        // _program : empty | _blocks ... ;
        private Boolean _program()
        {
            while (!tok.IsEOF())
            {
                if (_blocks()) continue;
                break;
            }
            return true;
        }

        // _scope : SCOPE_BEGIN _blocks SCOPE_END ;
        private Boolean _scope()
        {
            if (!Accept(TokenType.SCOPE_BEGIN)) return false;
            tok.MoveNext();
            if (!_blocks()) return false;
            if (Accept(TokenType.KOKOMADE)) tok.MoveNext();
            if (!Accept(TokenType.SCOPE_END))
            {
                throw new NakoParserException("トークンの終端がありません。システムエラー。", tok.CurrentToken);
            }
            tok.MoveNext(); // skip SCOPE_END
            return true;
        }

        // _blocks : _block ... 
        //         ;
        private Boolean _blocks()
        {
            if (tok.IsEOF()) return true;

            NakoToken tBlockTop = tok.CurrentToken;
            NakoToken t;

            while (!tok.IsEOF())
            {
                t = tok.CurrentToken;
                if (_statement()) continue;
                if (_eol()) continue;
                throw new NakoParserException("ブロックの解析エラー", t);
            }
            return true;
        }

        // _eol : EOL ;
        private Boolean _eol()
        {
            if (tok.IsEOF()) return false;
            if (Accept(TokenType.EOL))
            {
                tok.MoveNext(); return true;
            }
            return false;
        }

        // _statement : _let
        //            | _def_function
        //            | _if_stmt
        //            | _print
        //            | _callfunc
        //            ;
        private Boolean _statement()
        {
            if (tok.IsEOF()) return true;
            if (_def_function()) return true;
            if (_if_stmt()) return true;
            if (_let()) return true;
            if (_callfunc()) return true;
            if (_print()) return true;
            return false;
        }

        // _if_stmt : IF _value THEN [_statement]
        //          | IF _value THEN [_statement] ELSE [_statement]
        //          | IF _value THEN EOL [_scope]
        //          | IF _value THEN EOL [_scope] ELSE [_scope]
        //          ;
        private Boolean _if_stmt()
        {
            if (!Accept(TokenType.IF)) return false;
            tok.MoveNext(); // skip IF

            NakoNodeIf ifnode = new NakoNodeIf();

            // _value
            NakoToken t = tok.CurrentToken;
            if (!_value())
            {
                throw new NakoParserException("もし文で比較式がありません。", t);
            }
            ifnode.nodeCond = this.lastNode;
            
            // THEN
            if (Accept(TokenType.THEN)) tok.MoveNext();
            
            // 単文のとき
            if (!Accept(TokenType.EOL))
            {
                // TRUE
                this.PushNodeState();
                ifnode.nodeTrue = this.parentNode = this.lastNode = new NakoNode();
                _statement(); // true でも false でも OK
                this.PopNodeState();
                // FALSE
                if (Accept(TokenType.ELSE))
                {
                    tok.MoveNext(); // skip ELSE
                    this.PushNodeState();
                    ifnode.nodeFalse = this.parentNode = this.lastNode = new NakoNode();
                    _statement(); // true でも false でも OK
                    this.PopNodeState();
                }
            }
            else
            {
                tok.MoveNext(); // skip EOL

                // _scope
                // TRUE
                this.PushNodeState();
                ifnode.nodeTrue = this.parentNode = this.lastNode = new NakoNode();
                _scope(); // true でも false でも OK
                this.PopNodeState();
                // FALSE
                if (Accept(TokenType.ELSE))
                {
                    tok.MoveNext(); // skip ELSE
                    this.PushNodeState();
                    ifnode.nodeFalse = this.parentNode = this.lastNode = new NakoNode();
                    _scope(); // true でも false でも OK
                    this.PopNodeState();
                }
            }
            this.parentNode.AddChild(ifnode);
            this.lastNode = ifnode;
            return true;

        }

        // _callfunc : _value .. FUNCTION_NAME
        //           | FUNCTION_NAME
        //           ;
        private Boolean _callfunc()
        {
            NakoToken t = tok.CurrentToken;
            // TODO: 関数呼び出し
            tok.Save();
            while (!tok.IsEOF())
            {
                if (tok.CurrentTokenType == TokenType.FUNCTION_NAME)
                {
                    tok.MoveNext();
                    tok.RemoveTop();
                    return true;
                }
                if (!_value()) break;

            }
            tok.Restore();
            return false;
        }

        // _def_function : DEF_FUNCTION _def_function_args FUNCTION_NAME EOL _blocks
        //               ;
        private Boolean _def_function()
        {
            if (!Accept(TokenType.DEF_FUNCTION)) return false;
            NakoToken t = tok.CurrentToken;
            tok.MoveNext(); // '*'

            // 引数の取得
            _def_function_args();

            // 関数名
            if (!Accept(TokenType.FUNCTION_NAME))
            {
                throw new NakoParserException("関数の定義で関数名が見当たりません。", t);
            }
            tok.MoveNext(); // FUNCTION_NAME

            if (!Accept(TokenType.EOL))
            {
                throw new NakoParserException("関数の定義で改行がありません。", t);
            }
            tok.MoveNext(); // EOL

            // ブロックの取得
            PushFrame();
            NakoNodeDefFunction funcNode = new NakoNodeDefFunction();
            funcNode.type = NodeType.BLOCKS;
            parentNode = funcNode;
            funcNode.RegistArgsToLocalVar();
            localVar = funcNode.localVar;
            funcList.Add(funcNode);
            if (!_blocks())
            {
                throw new NakoParserException("関数定義中のエラー。", t);
            }
            PopFrame();
            return true;
        }

        // _def_function_args : empty
        //                    | '(' WORD ... ')' FUNCTION_NAME _blocks
        //                    | WORD ... FUNCTION_NAME _blocks
        //                    ;
        private Boolean _def_function_args()
        {
            if (Accept(TokenType.PARENTHESES_L))
            {
                tok.MoveNext();
            }
            // 引数の登録
            while (!tok.IsEOF()) {
                if (Accept(TokenType.PARENTHESES_R)) {
                    tok.MoveNext();
                    break;
                }
                if (Accept(TokenType.OR))
                {
                    tok.MoveNext();
                    break;
                }
                if (Accept(TokenType.WORD))
                {
                    // TODO: 引数の登録
                    tok.MoveNext();
                    continue;
                }
                break;
            }
            return true;
        }


        // _print : PRINT _value
        private Boolean _print()
        {
            if (tok.CurrentTokenType != TokenType.PRINT)
            {
                return false;
            }
            NakoNode n = new NakoNode();
            n.Token = tok.CurrentToken;
            tok.MoveNext();
            if (!_value())
            {
                throw new NakoParserException("PRINT の後に値がありません。", n.Token);
            }
            n.type = NodeType.PRINT;
            n.AddChild(this.lastNode);
            lastNode = n;
            this.parentNode.AddChild(n);
            return true;
        }

        // _let : _setVariable EQ _value
        private Boolean _let()
        {
            if (!_setVariable())
            {
                return false;
            }
            NakoNodeLet node = new NakoNodeLet();
            node.nodeVar = (NakoNodeVariable)lastNode;
 
            if (!Accept(TokenType.EQ))
            {
                return false;
            }
            tok.Save();
            tok.MoveNext();

            if (!_value())
            {
                tok.Restore();
                throw new NakoParserException("代入文で値がありません。", tok.CurrentToken);
            }
            node.AddChild(lastNode);
            parentNode.AddChild(node);
            lastNode = node;

            tok.RemoveTop();
            return true;
        }

        private void _variable__detectVariable(NakoNodeVariable n, String name)
        {
            // local ?
            if (localVar.ContainsKey(name))
            {
                n.scope = NakoVariableScope.Local;
                n.varNo = localVar[name];
            }
            else if (globalVar.ContainsKey(name))
            {
                n.scope = NakoVariableScope.Global;
                n.varNo = globalVar[name];
            }
            else
            {
                n.scope = NakoVariableScope.Global;
                n.varNo = globalVar.createName(name);
            }
        }

        // _setVariable : WORD '[' VALUE ']'
        //              | WORD ;
        private Boolean _setVariable()
        {
            if (!Accept(TokenType.WORD)) return false;

            // 配列アクセス
            if (tok.NextTokenType == TokenType.BLACKETS_L)
            {
                // TODO
                throw new NakoParserException("Not supported", tok.CurrentToken);
            }

            // 変数アクセス
            NakoNodeVariable n = new NakoNodeVariable();
            n.type = NodeType.ST_VARIABLE;
            n.Token = tok.CurrentToken;
            String name = (String)tok.CurrentToken.value;
            _variable__detectVariable(n, name);
            lastNode = n;
            tok.MoveNext();
            return true;
        }

        // _variable : WORD '[' VALUE ']'
        //           | WORD ;
        private Boolean _variable()
        {
            if (!Accept(TokenType.WORD)) return false;
            
            // 配列アクセス
            if (tok.NextTokenType == TokenType.BLACKETS_L)
            {
                // TODO
                throw new NakoParserException("Not supported", tok.CurrentToken);
            }

            // 変数アクセス
            NakoNodeVariable n = new NakoNodeVariable();
            n.type = NodeType.LD_VARIABLE;
            n.Token = tok.CurrentToken;

            String name = (String)tok.CurrentToken.value;
            _variable__detectVariable(n, name);
            lastNode = n;
            tok.MoveNext();
            return true;
        }

        // _value : _calc_formula | _calc_value ;
        private Boolean _value()
        {
            // TODO:
            // _value は再帰が多くコストが高いのであり得る値だけチェックする
            switch (tok.CurrentTokenType)
            {
                case TokenType.PARENTHESES_L:
                case TokenType.INT:
                case TokenType.NUMBER:
                case TokenType.WORD:
                case TokenType.FUNCTION_NAME:
                case TokenType.STRING:
                    break;
                default:
                    return false;
            }

            if (_calc_fact()) return true;
            return false;
        }

        // _calc_value : _const | _variable ;
        private Boolean _calc_value()
        {
            if (_const()) return true;
            if (_variable()) return true;
            return false;
        }

        // _calc_formula : PARENTHESES_L _value PARENTHESES_R 
        //               | _value
        private Boolean _calc_formula()
        {
            // Check '(' *** ')'
            if (Accept(TokenType.PARENTHESES_L))
            {
                tok.Save();
                tok.MoveNext();
                if (!_value())
                {
                    tok.Restore();
                    return false;
                }
                if (Accept(TokenType.PARENTHESES_R))
                {
                    tok.MoveNext();
                    tok.RemoveTop();
                    return true;
                }
                tok.Restore();
                return false;
            }
            return _calc_value();
        }

        // _calc_power : _calc_formula POWER _calc_formula
        //             ;
        private Boolean _calc_power()
        {
            NakoNodeCalc node = new NakoNodeCalc();
            if (!_calc_formula())
            {
                return false;
            }
            node.nodeL = lastNode;
            if (Accept(TokenType.POWER))
            {
                node.calc_type = CalcType.POWER;
                tok.Save();
                tok.MoveNext();
                if (!_calc_formula())
                {
                    tok.Restore();
                    return false;
                }
                tok.RemoveTop();
                node.nodeR = lastNode;
                lastNode = node;

                return true;
            }
            else
            {
                return true;
            }
        }

        // _calc_expr : _calc_power MUL _calc_power
        //            | _calc_power DIV _calc_power
        //            | _calc_power MOD _calc_power
        //            ;
        private Boolean _calc_expr()
        {
            NakoNodeCalc node = new NakoNodeCalc();
            if (!_calc_power()) { return false; }
            node.nodeL = lastNode;
            if (Accept(TokenType.MUL) || Accept(TokenType.DIV) || Accept(TokenType.MOD))
            {
                tok.Save();
                switch (tok.CurrentTokenType)
                {
                    case TokenType.MUL: node.calc_type = CalcType.MUL; break;
                    case TokenType.DIV: node.calc_type = CalcType.DIV; break;
                    case TokenType.MOD: node.calc_type = CalcType.MOD; break;
                }
                tok.MoveNext();
                if (!_calc_power())
                {
                    tok.Restore();
                    return false;
                }
                tok.RemoveTop();
                node.nodeR = lastNode;
                lastNode = node;
                return true;
            }
            else
            {
                return true;
            }
        }

        // _calc_term : _calc_expr PLUS _calc_expr 
        //            | _calc_expr MINUS _calc_expr
        //            ;
        private Boolean _calc_term()
        {
            NakoNodeCalc node = new NakoNodeCalc();
            if (!_calc_expr())
            {
                return false;
            }
            node.nodeL = lastNode;
            if (Accept(TokenType.PLUS) || Accept(TokenType.MINUS))
            {
                node.calc_type =
                    Accept(TokenType.PLUS) ? CalcType.ADD
                                             : CalcType.SUB;
                tok.Save();
                tok.MoveNext();
                if (!_calc_expr())
                {
                    tok.Restore();
                    return false;
                }
                tok.RemoveTop();
                node.nodeR = lastNode;
                lastNode = node;

                return true;
            }
            else
            {
                return true;
            }
        }

        // _calc_comp : _calc_term GT _calc_term
        //            | _calc_term LT _calc_term
        //            ;
        private Boolean _calc_comp()
        {
            NakoNodeCalc node = new NakoNodeCalc();
            if (!_calc_term())
            {
                return false;
            }

            node.nodeL = lastNode;
            if (Accept(TokenType.GT) ||
                Accept(TokenType.LT) ||
                Accept(TokenType.GT_EQ) ||
                Accept(TokenType.LT_EQ) ||
                Accept(TokenType.EQ) ||
                Accept(TokenType.EQ_EQ) ||
                Accept(TokenType.NOT_EQ))
            {
                switch (tok.CurrentToken.type)
                {
                    case TokenType.GT: node.calc_type = CalcType.GT; break;
                    case TokenType.LT: node.calc_type = CalcType.LT; break;
                    case TokenType.GT_EQ: node.calc_type = CalcType.GT_EQ; break;
                    case TokenType.LT_EQ: node.calc_type = CalcType.LT_EQ; break;
                    case TokenType.EQ: node.calc_type = CalcType.EQ; break;
                    case TokenType.EQ_EQ: node.calc_type = CalcType.EQ; break;
                    case TokenType.NOT_EQ: node.calc_type = CalcType.NOT_EQ; break;
                }
                tok.Save();
                tok.MoveNext();
                if (!_calc_term())
                {
                    tok.Restore();
                    return false;
                }
                tok.RemoveTop();
                node.nodeR = lastNode;
                lastNode = node;
                return true;
            }
            else
            {
                return true;
            }
        }

        // _calc_fact : MINUS _calc_fact
        //            | _calc_comp
        //            ;
        private Boolean _calc_fact()
        {
            NakoNodeCalc node = new NakoNodeCalc();
            node.Token = tok.CurrentToken;

            if (Accept(TokenType.MINUS))
            {
                node.calc_type = CalcType.NEG;
                tok.MoveNext();
                if (!_calc_fact())
                {
                    throw new NakoParserException("「-」記号の後に値がありません。", tok.CurrentToken);
                }
                node.nodeL = lastNode;
                tok.RemoveTop();
                lastNode = node;
                return true;
            }

            if (_calc_comp())
            {
                return true;
            }

            return false;
        }
        
        // _const : INT | NUMBER | STRING ;
        private Boolean _const()
        {
            NakoNodeConst node = new NakoNodeConst();
            node.Token = tok.CurrentToken;

            if (Accept(TokenType.INT))
            {
                node.type = NodeType.INT;
                node.value = Int32.Parse(node.Token.value);
                lastNode = node;
                tok.MoveNext();
                return true;
            }
            else if (Accept(TokenType.NUMBER))
            {
                node.type = NodeType.NUMBER;
                node.value = Double.Parse(node.Token.value);
                lastNode = node;
                tok.MoveNext();
                return true;
            }
            else if (Accept(TokenType.STRING))
            {
                node.type = NodeType.STRING;
                node.value = node.Token.value;
                lastNode = node;
                tok.MoveNext();
                return true;
            }

            return false;
        }
    }
}
