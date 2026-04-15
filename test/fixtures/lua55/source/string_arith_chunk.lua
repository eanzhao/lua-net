local fallback = setmetatable({}, {
    __add = function(left, right)
        return "fallback"
    end
})

local add = "10" + 1
local sub = "10" - 1
local mul = "6" * 7
local mod = "9" % 4
local pow = "2" ^ 3
local div = "7" / 2
local idiv = "7" // 2
local unm = -"5"
local delegated = "x" + fallback

return add, sub, mul, mod, pow, div, idiv, unm, delegated
