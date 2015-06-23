(function () {
    window.quest = window.quest || {};
    
    window.quest.load = function (data) {
        // TODO: Eventually this will be called with a full .aslx file
        // (Note - only a single file from a .quest, we don't need to worry ever about including
        // external libraries)

        var script = parseScript(data);
        console.log(script);
        executeScript(script);
    };

    var scripts = {
        "msg": {
            parameters: [1]
        },
        "if": {
            create: function (line) {
                var parameters = getParameterInternal(line, '(', ')');
                var thenScript = parseScript(parameters.after);

                return {
                    expression: parameters.parameter,
                    then: thenScript
                };
            }
        }
    };

    var getScript = function (line) {
        // based on WorldModel.ScriptFactory.GetScriptConstructor

        var script, keyword, parameters;

        for (var candidate in scripts) {
            if (line.substring(0, candidate.length) === candidate) {
                // TODO: Must be non-word character afterwards, see original function
                keyword = candidate;
                script = scripts[candidate];
            }
        }

        if (!script) return null;

        if (script.create) {
            parameters = script.create(line);
        }
        else {
            parameters = splitParameters(line);
            if (script.parameters.indexOf(parameters.length) === -1) {
                throw 'Expected ' + script.parameters.join(',') + ' parameters in script: ' + line;
            }
        }

        return {
            keyword: keyword,
            script: script,
            line: line,
            parameters: parameters
        };
    };

    var parseScript = function (text) {
        text = removeSurroundingBraces(text);

        var result = [];
        while (true) {
            var scriptLine = getScriptLine(text);

            if (!scriptLine) break;
            if (scriptLine.line.length !== 0) {
                var script = getScript(scriptLine.line);
                
                if (!script) {
                    console.log('Unrecognised script command: ' + scriptLine.line);
                }
                else {
                    result.push(script);
                }
            }

            if (!scriptLine.after) break;
            text = scriptLine.after;
        }

        return result;
    };

    var removeSurroundingBraces = function (text) {
        // based on WorldModel.Utility.RemoveSurroundingBraces

        text = text.trim();
        if (text.substring(0, 1) === '{' && text.substring(text.length - 1, text.length) === '}') {
            return text.substring(1, text.length - 1);
        }
        return text;
    };

    var getScriptLine = function (text) {
        // based on WorldModel.Utility.GetScript
        // return one line of the script, and the remaining script

        var obscuredScript = obscureStrings(text);
        var bracePos = obscuredScript.indexOf('{');
        var crlfPos = obscuredScript.indexOf('\n');
        var commentPos = obscuredScript.indexOf('//');
        if (crlfPos === -1) return {
            line: text.trim()
        };

        if (bracePos === - 1 || crlfPos < bracePos || (commentPos !== -1 && commentPos < bracePos && commentPos < crlfPos)) {
            return {
                line: text.substring(0, crlfPos).trim(),
                after: text.substring(crlfPos + 1)
            };
        }

        var beforeBrace = text.substring(0, bracePos);
        var parameterResult = getParameterInternal(text, '{', '}');
        var insideBraces = parameterResult.parameter;

        if (insideBraces.indexOf('\n') !== -1) {
            result = beforeBrace + '{' + insideBraces + '}';
        }
        else {
            result = beforeBrace + insideBraces;
        }

        return {
            line: result.trim(),
            after: parameterResult.after
        };
    };

    var splitParameters = function (text) {
        var parameter = getParameter(text);
        if (!parameter) return [];

        // based on WorldModel.Utility.SplitParameter
        var result = [];
        var inQuote = false;
        var processNextCharacter = true;
        var bracketCount = 0;
        var curParam = [];

        for (var i = 0; i < parameter.length; i++) {
            var c = parameter.charAt(i);
            var processThisCharacter = processNextCharacter;
            processNextCharacter = true;

            if (processThisCharacter) {
                if (c === '\\') {
                    // Don't process the character after a backslash
                    processNextCharacter = false;
                }
                else if (c === '"') {
                    inQuote = !inQuote;
                }
                else {
                    if (!inQuote) {
                        if (c === '(') bracketCount++;
                        if (c === ')') bracketCount--;
                        if (bracketCount === 0 && c === ',') {
                            result.push(curParam.join(''));
                            curParam = [];
                            continue;
                        }
                    }
                }
            }

            curParam.push(c);
        }

        result.push(curParam.join('').trim());
        return result;
    };

    var getParameter = function (text) {
        var result = getParameterInternal(text, '(', ')');
        if (!result) return null;
        return result.parameter;
    };

    var getParameterInternal = function (text, open, close) {
        // based on WorldModel.Utility.GetParameterInt

        var afterParameter = null;
        var obscuredText = obscureStrings(text);
        var start = obscuredText.indexOf(open);
        if (start === -1) return null;

        var finished = false;
        var braceCount = 1;
        var pos = start;

        while (!finished) {
            pos++;
            var curChar = obscuredText.charAt(pos);
            if (curChar === open) braceCount++;
            if (curChar === close) braceCount--;
            if (braceCount === 0 || pos === obscuredText.length - 1) finished = true;
        }

        if (braceCount !== 0) {
            throw 'Missing ' + close;
        }

        return {
            parameter: text.substring(start + 1, pos),
            after: text.substring(pos + 1)
        }
    };

    var obscureStrings = function (input) {
        // based on WorldModel.Utility.ObscureStrings

        var sections = splitQuotes(input);
        var result = [];

        var insideQuote = false;
        for (var i = 0; i < sections.length; i++) {
            var section = sections[i];
            if (insideQuote) {
                result.push(Array(section.length + 1).join('-'));
            }
            else {
                result.push(section);
            }
            if (i < sections.length - 1) {
                result.push('"');
            }
            insideQuote = !insideQuote;
        }
        return result.join('');
    };

    var splitQuotes = function (text) {
        // based on WorldModel.Utility.SplitQuotes

        var result = [];
        var processNextCharacter = true;
        var curParam = [];
        var gotCloseQuote = true;

        for (var i = 0; i < text.length; i++) {
            var curChar = text.charAt(i);

            var processThisCharacter = processNextCharacter;
            processNextCharacter = true;

            if (processThisCharacter) {
                if (curChar === '\\') {
                    // Don't process the character after a backslash
                    processNextCharacter = false;
                }
                else if (curChar === '"') {
                    result.push(curParam.join(''));
                    gotCloseQuote = !gotCloseQuote;
                    curParam = [];
                    continue;
                }
            }

            curParam.push(curChar);
        }

        if (!gotCloseQuote) {
            throw 'Missing quote character in ' + text;
        }

        result.push(curParam.join(''));
        return result;
    };

    var executeScript = function (script) {
        console.log("executeScript not yet implemented");
    };
})();