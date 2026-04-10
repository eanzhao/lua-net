local t = setmetatable({}, { __metatable = "locked" })

return getmetatable(t)
