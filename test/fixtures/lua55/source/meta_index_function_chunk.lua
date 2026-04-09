local t = setmetatable({ prefix = "lua" }, {
  __index = function(self, key)
    return self.prefix .. "-" .. key
  end
})

return t.net
