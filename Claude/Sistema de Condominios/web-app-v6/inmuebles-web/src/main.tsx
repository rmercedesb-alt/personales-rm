import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

// Proteccion global contra el crash de React 19 "removeChild/insertBefore:
// el nodo no es hijo de este nodo" que ocurre al filtrar/borrar rapido en
// modo concurrente (o por extensiones del navegador que tocan el DOM).
// Si el nodo de referencia ya no pertenece al padre, se evita la excepcion.
if (typeof Node === 'function' && Node.prototype) {
  const _removeChild = Node.prototype.removeChild
  Node.prototype.removeChild = function <T extends Node>(child: T): T {
    if (child.parentNode !== this) return child
    return _removeChild.call(this, child) as T
  }
  const _insertBefore = Node.prototype.insertBefore
  Node.prototype.insertBefore = function <T extends Node>(newNode: T, referenceNode: Node | null): T {
    if (referenceNode && referenceNode.parentNode !== this) return newNode
    return _insertBefore.call(this, newNode, referenceNode) as T
  }
}

createRoot(document.getElementById('root')!).render(<App />)
